namespace CameraTool.WebStream.Settings;

public sealed class MotionSettings
{
    public bool Enable { get; set; } = true;

    // Luma difference from the background to be a changed pixel (smaller is more sensitive)
    [Range(0, 255)]
    public int Threshold { get; set; } = 25;

    // Ratio of changed pixels in a cell to be a moving cell
    [Range(0.0, 1.0)]
    public float CellRatio { get; set; } = 0.2f;

    // Ratio of moving cells to the cells in the detection area to be motion
    [Range(0.0, 1.0)]
    public float MinArea { get; set; } = 0.005f;

    // Update rate of the background per frame
    [Range(0.0, 1.0)]
    public float BackgroundRate { get; set; } = 0.05f;
}
