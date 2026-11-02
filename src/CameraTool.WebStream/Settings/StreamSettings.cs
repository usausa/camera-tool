namespace CameraTool.WebStream.Settings;

public sealed class StreamSettings
{
    // JPEG quality
    [Range(1, 100)]
    public int Quality { get; set; } = 80;
}
