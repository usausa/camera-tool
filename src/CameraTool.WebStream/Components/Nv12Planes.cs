namespace CameraTool.WebStream.Components;

using CameraTool.Capture;
using CameraTool.Vision;

// NV12 planes of a frame for an encoder, converted into an own buffer unless the frame is NV12 in the range.
// Full range for JPEG, or the range of the frame for H.264 (written to the stream).
// Used by one thread (each encoder has its own).
public sealed class Nv12Planes
{
    private readonly bool fullRange;

    private byte[] buffer = [];

    public Nv12Planes(bool fullRange)
    {
        this.fullRange = fullRange;
    }

    // False for an unsupported format, the planes are valid until the next call (or while the frame is valid)
    public bool TryGet(in FrameBuffer frame, out ReadOnlySpan<byte> y, out int yStride, out ReadOnlySpan<byte> uv, out int uvStride)
    {
        var width = frame.Width;
        var height = frame.Height;
        switch (frame.PixelFormat)
        {
            case VideoPixelFormat.Nv12 when !fullRange || (frame.ColorRange == VideoColorRange.Full):
                // Passed without copy
                y = frame.GetPlaneSpan(frame.Plane0);
                yStride = frame.Plane0.Stride;
                uv = frame.GetPlaneSpan(frame.Plane1);
                uvStride = frame.Plane1.Stride;
                return true;
            case VideoPixelFormat.Nv12:
            {
                // YUV without range information is treated as video range
                var stride = Prepare(width, height);
                var destinationY = buffer.AsSpan(0, stride * height);
                var destinationUv = buffer.AsSpan(stride * height);
                ImageConverter.Nv12ToFullRange(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, frame.GetPlaneSpan(frame.Plane1), frame.Plane1.Stride, destinationY, stride, destinationUv, stride, width, height);
                y = destinationY;
                uv = destinationUv;
                yStride = stride;
                uvStride = stride;
                return true;
            }

            case VideoPixelFormat.Yuy2:
            {
                var stride = Prepare(width, height);
                var destinationY = buffer.AsSpan(0, stride * height);
                var destinationUv = buffer.AsSpan(stride * height);
                if (fullRange)
                {
                    var range = frame.ColorRange == VideoColorRange.Full ? YuvRange.Full : YuvRange.Limited;
                    ImageConverter.Yuy2ToNv12(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, destinationY, stride, destinationUv, stride, width, height, range);
                }
                else
                {
                    ImageConverter.Yuy2ToNv12(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, destinationY, stride, destinationUv, stride, width, height);
                }

                y = destinationY;
                uv = destinationUv;
                yStride = stride;
                uvStride = stride;
                return true;
            }

            default:
                y = default;
                uv = default;
                yStride = 0;
                uvStride = 0;
                return false;
        }
    }

    // Returns the stride of both planes
    private int Prepare(int width, int height)
    {
        var stride = ((width + 1) / 2) * 2;
        var size = (stride * height) + (stride * ((height + 1) / 2));
        if (buffer.Length != size)
        {
            buffer = GC.AllocateUninitializedArray<byte>(size);
        }

        return stride;
    }
}
