namespace CameraTool.FaceViewer.Settings;

using CameraTool.Vision;

public sealed class ObjectSettings : IValidatableObject
{
    public bool Enable { get; set; }

    [Required]
    public string Model { get; set; } = "dfine-nano-coco.onnx";

    [Range(0.0, 1.0)]
    public float ConfidenceThreshold { get; set; } = 0.5f;

    [Range(0, 64)]
    public int IntraOpNumThreads { get; set; } = 1;

    [Range(0, 64)]
    public int InterOpNumThreads { get; set; } = 1;

    // Milliseconds between the inputs (0: as soon as the detector is idle)
    [Range(0, 60000)]
    public int Interval { get; set; } = 200;

    // COCO class names separated by commas (empty: all classes)
    public string Classes { get; set; } = "person,bird,cat,dog,bicycle,car,motorcycle,bus,truck";

    public IReadOnlyList<int> GetClassIds() => SplitClasses().Select(ObjectLabels.IndexOf).ToArray();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var name in SplitClasses())
        {
            if (ObjectLabels.IndexOf(name) < 0)
            {
                yield return new ValidationResult($"Unknown class. name=[{name}]", [nameof(Classes)]);
            }
        }
    }

    private string[] SplitClasses() => Classes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
