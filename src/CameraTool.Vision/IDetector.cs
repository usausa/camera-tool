namespace CameraTool.Vision;

// Detector with an input tensor of a fixed size (FaceDetector, ObjectDetector)
public interface IDetector<TBox> : IDisposable
    where TBox : struct
{
    int InputWidth { get; }

    int InputHeight { get; }

    // CHW RGB tensor written before Detect
    Span<float> Input { get; }

    int Detect(Span<TBox> results);
}
