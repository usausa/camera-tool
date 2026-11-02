namespace CameraTool.WebStream.Components;

using CameraTool.Vision;
using CameraTool.WebStream.Settings;

using Microsoft.Extensions.Logging.Abstractions;

public sealed class AreaServiceTests : IDisposable
{
    private const int LastColumn = DetectionArea.Columns - 1;

    private const int LastRow = DetectionArea.Rows - 1;

    private readonly string directory = Path.Combine(Path.GetTempPath(), "CameraTool.WebStream.Tests", Guid.NewGuid().ToString("N"));

    private string AreaPath => Path.Combine(directory, "area.json");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    //--------------------------------------------------------------------------------
    // Load
    //--------------------------------------------------------------------------------

    [Fact]
    public void MissingFileGivesFullArea() =>
        Assert.True(CreateService(AreaPath).Current.IsFull);

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"columns":16,"rows":24,"cells":[]}""")]
    [InlineData("""{"columns":32,"rows":24,"cells":["1"]}""")]
    public void InvalidFileGivesFullArea(string content)
    {
        // Arrange
        Directory.CreateDirectory(directory);
        File.WriteAllText(AreaPath, content);

        // Act
        var service = CreateService(AreaPath);

        // Assert
        Assert.True(service.Current.IsFull);
    }

    //--------------------------------------------------------------------------------
    // Save
    //--------------------------------------------------------------------------------

    [Fact]
    public void SavedAreaIsLoadedAgain()
    {
        // Arrange (the right half is outside)
        var service = CreateService(AreaPath);
        var area = DetectionArea.Full.WithCells(16, 0, LastColumn, LastRow, false);
        service.Update(area);

        // Act
        var saved = service.Save();
        var loaded = CreateService(AreaPath);

        // Assert
        Assert.True(saved);
        Assert.Equal(area.ToRows(), loaded.Current.ToRows());
        Assert.False(File.Exists(AreaPath + ".tmp"));
        // Indented so that it can be edited by hand
        Assert.Contains("\n  \"columns\": 32,", File.ReadAllText(AreaPath), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveFailsWhenFileCannotBeWritten()
    {
        // Arrange (the folder of the file is a file)
        Directory.CreateDirectory(directory);
        var blocker = Path.Combine(directory, "blocker");
        File.WriteAllText(blocker, string.Empty);
        var service = CreateService(Path.Combine(blocker, "area.json"));
        var area = DetectionArea.Full.WithCells(0, 0, 3, 3, false);
        service.Update(area);

        // Act
        var saved = service.Save();

        // Assert (the area is kept until the server stops)
        Assert.False(saved);
        Assert.Same(area, service.Current);
    }

    [Fact]
    public void UpdateIncrementsVersion()
    {
        // Arrange
        var service = CreateService(AreaPath);
        var version = service.Version;

        // Act
        service.Update(DetectionArea.Full.WithCells(0, 0, 0, 0, false));

        // Assert
        Assert.Equal(version + 1, service.Version);
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static AreaService CreateService(string path) =>
        new(NullLogger<AreaService>.Instance, new AreaSettings { Path = path });
}
