namespace CameraTool.Vision;

// Statistics of the face or object detection (the same items for both)
public interface IDetectionStatistics
{
    bool IsEnabled { get; }

    // Set when the model cannot be loaded
    string? ErrorMessage { get; }

    long DetectedFrames { get; }

    // Results of the latest detection
    int Count { get; }

    LatencySnapshot TakePreprocessTime();

    LatencySnapshot TakeInferenceTime();
}
