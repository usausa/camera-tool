namespace CameraTool.Vision;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

public enum DetectionAreaFileResult
{
    NotFound,
    Loaded,
    Invalid
}

// Saved form of the detection area: Cells are the rows from the top ('1': in the area, '0': outside)
public sealed class DetectionAreaData
{
    public int Columns { get; set; }

    public int Rows { get; set; }

    public IReadOnlyList<string> Cells { get; set; } = [];

    public static DetectionAreaData From(DetectionArea area) =>
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

// File of the detection area (JSON, shared by the applications)
public static class DetectionAreaFile
{
    // area: the full area unless loaded.
    // Throws IOException, UnauthorizedAccessException or JsonException when the file cannot be read.
    public static DetectionAreaFileResult Read(string path, out DetectionArea area)
    {
        area = DetectionArea.Full;
        if (!File.Exists(path))
        {
            return DetectionAreaFileResult.NotFound;
        }

        // Read through a stream, which skips the BOM of a file saved as UTF-8 with a BOM (the span overload does not)
        using var stream = File.OpenRead(path);
        var data = JsonSerializer.Deserialize(stream, DetectionAreaJsonContext.Default.DetectionAreaData);
        if ((data is null) || !data.TryGetArea(out var loaded))
        {
            return DetectionAreaFileResult.Invalid;
        }

        area = loaded;
        return DetectionAreaFileResult.Loaded;
    }

    // Written to a temporary file and replaced, so the file is not broken when the process stops while writing.
    // Throws IOException or UnauthorizedAccessException when the file cannot be written.
    public static void Write(string path, DetectionArea area)
    {
        if (Path.GetDirectoryName(path) is { } directory)
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(DetectionAreaData.From(area), DetectionAreaJsonContext.Default.DetectionAreaData));
        File.Move(temporary, path, true);
    }
}

// The file is indented so that it can be edited by hand
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(DetectionAreaData))]
internal sealed partial class DetectionAreaJsonContext : JsonSerializerContext;
