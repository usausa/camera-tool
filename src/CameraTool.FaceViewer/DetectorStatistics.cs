namespace CameraTool.FaceViewer;

using CameraTool.Vision;

// Statistics of the face or object detection shown in the panel (the same items for both), updated on the UI thread
public sealed partial class DetectorStatistics : ObservableObject
{
    private long lastDetectedFrames;

    [ObservableProperty]
    public partial double Fps { get; set; }

    [ObservableProperty]
    public partial LatencyValue PreprocessTime { get; set; }

    [ObservableProperty]
    public partial LatencyValue InferenceTime { get; set; }

    // Count of the results (and the count of each class for the objects)
    [ObservableProperty]
    public partial string Results { get; set; } = "0";

    // Set when the model cannot be loaded
    [ObservableProperty]
    public partial string? Error { get; set; }

    // At the start of the statistics
    public void Reset(IDetectionStatistics detector) => lastDetectedFrames = detector.DetectedFrames;

    // elapsed: seconds since the last update
    public void Update(IDetectionStatistics detector, double elapsed, string results)
    {
        var detectedFrames = detector.DetectedFrames;
        Fps = (detectedFrames - lastDetectedFrames) / elapsed;
        lastDetectedFrames = detectedFrames;
        PreprocessTime = LatencyValue.From(detector.TakePreprocessTime());
        InferenceTime = LatencyValue.From(detector.TakeInferenceTime());
        Results = results;
        Error = detector.ErrorMessage;
    }
}
