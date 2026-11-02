namespace CameraTool.FaceViewer.Settings;

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

    // Detect only on motion: while there is no motion, the input is prepared once per IdleInterval
    public bool MotionGate { get; set; }

    // Milliseconds
    [Range(0, 60000)]
    public int IdleInterval { get; set; } = 1000;

    // Milliseconds the motion is kept after the last moving frame
    [Range(0, 60000)]
    public int MotionHoldTime { get; set; } = 2000;
}
