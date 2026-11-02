namespace CameraTool.Vision;

public sealed class MotionDetectorOptions
{
    // Luma difference from the background to be a changed pixel (smaller is more sensitive)
    public int Threshold { get; set; } = 25;

    // Ratio of changed pixels in a cell to be a moving cell
    public float CellRatio { get; set; } = 0.2f;

    // Ratio of moving cells to the cells in the detection area to be motion
    public float MinArea { get; set; } = 0.005f;

    // Update rate of the background per frame
    public float BackgroundRate { get; set; } = 0.05f;
}
