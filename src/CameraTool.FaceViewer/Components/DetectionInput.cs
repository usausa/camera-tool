namespace CameraTool.FaceViewer.Components;

using CameraTool.Capture;
using CameraTool.Vision;

// Tensor input of a detector from a frame (resized, RGB, normalized), shared by the face and the object detection
public static class DetectionInput
{
    // False for an unsupported format or an unexpected buffer layout
    public static bool TryPrepare(in FrameBuffer frame, Span<float> input, int width, int height, TensorNormalization normalization)
    {
        var range = frame.ColorRange == VideoColorRange.Full ? YuvRange.Full : YuvRange.Limited;
        try
        {
            switch (frame.PixelFormat)
            {
                case VideoPixelFormat.Bgra8:
                    TensorConverter.BgraToTensor(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, frame.Width, frame.Height, input, width, height, normalization);
                    return true;
                case VideoPixelFormat.Nv12:
                    TensorConverter.Nv12ToTensor(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, frame.GetPlaneSpan(frame.Plane1), frame.Plane1.Stride, frame.Width, frame.Height, range, input, width, height, normalization);
                    return true;
                case VideoPixelFormat.Yuy2:
                    TensorConverter.Yuy2ToTensor(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, frame.Width, frame.Height, range, input, width, height, normalization);
                    return true;
                default:
                    return false;
            }
        }
        catch (ArgumentException)
        {
            // Unexpected buffer layout
            return false;
        }
    }
}
