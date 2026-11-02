namespace CameraTool.FaceViewer.Components;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

using CameraTool.FaceViewer.Settings;
using CameraTool.Vision;

// Saved form of the detection area: Cells are the rows from the top ('1': in the area, '0': outside)
public sealed class AreaData
{
    public int Columns { get; set; }

    public int Rows { get; set; }

    public IReadOnlyList<string> Cells { get; set; } = [];

    public static AreaData From(DetectionArea area) =>
        new()
        {
            Columns = DetectionArea.Columns,
            Rows = DetectionArea.Rows,
            Cells = area.ToRows()
        };

    public bool TryGetArea([NotNullWhen(true)] out DetectionArea? area)
    {
        if ((Columns != DetectionArea.Columns) || (Rows != DetectionArea.Rows))
        {
            area = null;
            return false;
        }

        return DetectionArea.TryParse(Cells, out area);
    }
}

// The file is indented so that it can be edited by hand
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(AreaData))]
public sealed partial class AreaJsonContext : JsonSerializerContext;

// Holds the detection area shared by the motion and face detection, and saves it to a file.
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

    // Written to a temporary file and replaced, so the file is not broken when the application stops while writing
    public void Save()
    {
        lock (saveSync)
        {
            var area = current;
            var temporary = path + ".tmp";
            try
            {
                if (Path.GetDirectoryName(path) is { } directory)
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(AreaData.From(area), AreaJsonContext.Default.AreaData));
                File.Move(temporary, path, true);
                log.InfoAreaSaved(path, area.CellCount);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.ErrorAreaSaveFailed(ex, path);
            }
        }
    }

    // The full area is used when the file does not exist or is invalid
    private DetectionArea Load()
    {
        if (!File.Exists(path))
        {
            return DetectionArea.Full;
        }

        try
        {
            var data = JsonSerializer.Deserialize(File.ReadAllBytes(path), AreaJsonContext.Default.AreaData);
            if ((data is not null) && data.TryGetArea(out var area))
            {
                log.InfoAreaLoaded(path, area.CellCount);
                return area;
            }

            log.WarnAreaInvalid(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            log.WarnAreaLoadFailed(ex, path);
        }

        return DetectionArea.Full;
    }
}
