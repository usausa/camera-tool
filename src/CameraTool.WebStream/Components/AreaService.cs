namespace CameraTool.WebStream.Components;

using System.Text.Json;

using CameraTool.Vision;
using CameraTool.WebStream.Settings;

// Holds the detection area shared by the motion and face detection, and saves it to a file (DetectionAreaFile).
// The area is immutable and replaced as a whole, so the capture and detection threads read it without a lock.
public sealed class AreaService
{
    private readonly ILogger<AreaService> log;

    private readonly string path;

    private readonly Lock saveSync = new();

    private volatile DetectionArea current;

    // Starts at a value that differs between the server runs, so that a viewer also gets the area again after a restart
    private int version = Environment.TickCount;

    public DetectionArea Current => current;

    // Incremented on each change, the viewers get the area again when it changes
    public int Version => Volatile.Read(ref version);

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
        Interlocked.Increment(ref version);
    }

    // The file is not broken when the server stops while writing (see DetectionAreaFile).
    // Serialized because the viewers can update the area at the same time.
    // False when the file cannot be written (the area is used until the server stops).
    public bool Save()
    {
        lock (saveSync)
        {
            var area = current;
            try
            {
                DetectionAreaFile.Write(path, area);
                log.InfoAreaSaved(path, area.CellCount);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.ErrorAreaSaveFailed(ex, path);
                return false;
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
