namespace CameraTool.WebStream.Components;

using CameraTool.Capture;
using CameraTool.Vision;

// Full range NV12 planes of a frame for the JPEG encoder, converted into an own buffer unless the frame is full range NV12.
// Used by one thread. (H.264 takes the frame in its own range, see H264Encoder.)
public sealed class Nv12Planes
{
    private byte[] buffer = [];

    // False for an unsupported format, the planes are valid until the next call (or while the frame is valid)
    public bool TryGet(in FrameBuffer frame, out ReadOnlySpan<byte> y, out int yStride, out ReadOnlySpan<byte> uv, out int uvStride)
    {
        var width = frame.Width;
        var height = frame.Height;
        switch (frame.PixelFormat)
        {
            case VideoPixelFormat.Nv12 when frame.ColorRange == VideoColorRange.Full:
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
                var range = frame.ColorRange == VideoColorRange.Full ? YuvRange.Full : YuvRange.Limited;
                ImageConverter.Yuy2ToNv12(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, destinationY, stride, destinationUv, stride, width, height, range);
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
