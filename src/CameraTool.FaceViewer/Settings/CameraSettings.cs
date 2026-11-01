namespace CameraTool.FaceViewer.Settings;

using CameraTool.Capture;

public sealed class CameraSettings
{
    // Empty: first device
    public string Name { get; set; } = string.Empty;

    public VideoPixelFormat Format { get; set; } = VideoPixelFormat.Yuy2;

    [Range(1, 8192)]
    public int Width { get; set; } = 640;

    [Range(1, 8192)]
    public int Height { get; set; } = 480;

    [Range(1, 240)]
    public int Fps { get; set; } = 30;
}
