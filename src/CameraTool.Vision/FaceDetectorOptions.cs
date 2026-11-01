namespace CameraTool.Vision;

public sealed class FaceDetectorOptions
{
    public float ConfidenceThreshold { get; set; } = 0.7f;

    public float IouThreshold { get; set; } = 0.3f;

    public int IntraOpNumThreads { get; set; } = 1;

    public int InterOpNumThreads { get; set; } = 1;
}
