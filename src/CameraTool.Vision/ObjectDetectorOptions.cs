namespace CameraTool.Vision;

public sealed class ObjectDetectorOptions
{
    public float ConfidenceThreshold { get; set; } = 0.5f;

    // Class ids to detect (empty: all classes)
    public IReadOnlyList<int> Classes { get; set; } = [];

    public int IntraOpNumThreads { get; set; } = 1;

    public int InterOpNumThreads { get; set; } = 1;
}
