namespace CameraTool.FaceViewer.Services;

using System.Text.Json;

using CameraTool.FaceViewer.Settings;
using CameraTool.Vision;

// Holds the detection area shared by the motion and face detection, and saves it to a file (DetectionAreaFile).
// The area is immutable and replaced as a whole, so the capture and detection threads read it without a lock.
public sealed class AreaService
{
    private readonly ILogger<AreaService> log;

    private readonly string path;

    private readonly Lock saveSync = new();

    private volatile DetectionArea current;

    // Raised on the thread that changed the area (the UI thread)
    public event EventHandler? Changed;

    public DetectionArea Current => current;

    // Set when the last save failed (the area is used until the application stops)
    public string? ErrorMessage { get; private set; }

    public AreaService(ILogger<AreaService> log, AreaSettings settings)
    {
        this.log = log;
        path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.Path));
        current = Load();
    }

    // Applied to the next frame
    public void Update(DetectionArea area)
    {
        current = area;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // The file is not broken when the application stops while writing (see DetectionAreaFile)
    public void Save()
    {
        lock (saveSync)
        {
            var area = current;
            try
            {
                DetectionAreaFile.Write(path, area);
                log.InfoAreaSaved(path, area.CellCount);
                ErrorMessage = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.ErrorAreaSaveFailed(ex, path);
                ErrorMessage = String.Create(CultureInfo.InvariantCulture, $"Area not saved: {ex.Message}");
            }
        }
    }

    // The full area is used when the file does not exist or is invalid
    private DetectionArea Load()
    {
        try
        {
            var result = DetectionAreaFile.Read(path, out var area);
            if (result == DetectionAreaFileResult.Loaded)
            {
                log.InfoAreaLoaded(path, area.CellCount);
            }
            else if (result == DetectionAreaFileResult.Invalid)
            {
                log.WarnAreaInvalid(path);
            }

            return area;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            log.WarnAreaLoadFailed(ex, path);
            return DetectionArea.Full;
        }
    }
}
