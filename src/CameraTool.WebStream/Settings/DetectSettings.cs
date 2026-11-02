namespace CameraTool.WebStream.Settings;

public sealed class DetectSettings
{
    public bool Enable { get; set; } = true;

    [Required]
    public string Model { get; set; } = "version-RFB-320.onnx";

    [Range(0.0, 1.0)]
    public float ConfidenceThreshold { get; set; } = 0.7f;

    [Range(0.0, 1.0)]
    public float IouThreshold { get; set; } = 0.3f;

    [Range(0, 64)]
    public int IntraOpNumThreads { get; set; } = 1;

    [Range(0, 64)]
    public int InterOpNumThreads { get; set; } = 1;
}
