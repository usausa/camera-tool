namespace CameraTool.FaceViewer.Settings;

public sealed class AreaSettings
{
    // File of the detection area (environment variables are expanded, a relative path is from the application directory)
    [Required]
    public string Path { get; set; } = @"%LOCALAPPDATA%\CameraTool\FaceViewer\area.json";
}
