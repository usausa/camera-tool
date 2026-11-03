namespace CameraTool.Vision;

using System.Text;
using System.Text.Json;

public sealed class DetectionAreaFileTests : IDisposable
{
    private const int LastColumn = DetectionArea.Columns - 1;

    private const int LastRow = DetectionArea.Rows - 1;

    private readonly string directory = Path.Combine(Path.GetTempPath(), "CameraTool.Vision.Tests", Guid.NewGuid().ToString("N"));

    private string AreaPath => Path.Combine(directory, "area.json");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    //--------------------------------------------------------------------------------
    // Read
    //--------------------------------------------------------------------------------

    [Fact]
    public void WrittenAreaIsRead()
    {
        // Arrange (the bottom half is outside)
        var area = DetectionArea.Full.WithCells(0, 12, LastColumn, LastRow, false);

        // Act
        DetectionAreaFile.Write(AreaPath, area);
        var result = DetectionAreaFile.Read(AreaPath, out var read);

        // Assert
        Assert.Equal(DetectionAreaFileResult.Loaded, result);
        Assert.Equal(area.ToRows(), read.ToRows());
        Assert.False(File.Exists(AreaPath + ".tmp"));
        // Indented so that it can be edited by hand
        Assert.Contains("\n  \"columns\": 32,", File.ReadAllText(AreaPath), StringComparison.Ordinal);
    }

    [Fact]
    public void EditedFileIsRead()
    {
        // Arrange (a comment and a trailing comma, as in a file edited by hand)
        Directory.CreateDirectory(directory);
        var rows = String.Join(", ", Enumerable.Repeat($"\"{new string('0', DetectionArea.Columns)}\"", DetectionArea.Rows));
        File.WriteAllText(AreaPath, $$"""
            {
              // Nothing is in the area
              "columns": 32,
              "rows": 24,
              "cells": [{{rows}}],
            }
            """);

        // Act
        var result = DetectionAreaFile.Read(AreaPath, out var area);

        // Assert
        Assert.Equal(DetectionAreaFileResult.Loaded, result);
        Assert.Equal(0, area.CellCount);
    }

    [Fact]
    public void FileWithByteOrderMarkIsRead()
    {
        // Arrange (UTF-8 with a BOM, as some editors save the file)
        var area = DetectionArea.Full.WithCells(0, 0, LastColumn, 11, false);
        DetectionAreaFile.Write(AreaPath, area);
        File.WriteAllBytes(AreaPath, [.. Encoding.UTF8.GetPreamble(), .. File.ReadAllBytes(AreaPath)]);

        // Act
        var result = DetectionAreaFile.Read(AreaPath, out var read);

        // Assert
        Assert.Equal(DetectionAreaFileResult.Loaded, result);
        Assert.Equal(area.ToRows(), read.ToRows());
    }

    [Fact]
    public void MissingFileIsNotFound()
    {
        // Act
        var result = DetectionAreaFile.Read(AreaPath, out var area);

        // Assert
        Assert.Equal(DetectionAreaFileResult.NotFound, result);
        Assert.True(area.IsFull);
    }

    [Theory]
    [InlineData("""{"columns":16,"rows":24,"cells":[]}""")]
    [InlineData("""{"columns":32,"rows":24,"cells":["1"]}""")]
    [InlineData("null")]
    public void OtherAreaIsInvalid(string content)
    {
        // Arrange
        Directory.CreateDirectory(directory);
        File.WriteAllText(AreaPath, content);

        // Act
        var result = DetectionAreaFile.Read(AreaPath, out var area);

        // Assert
        Assert.Equal(DetectionAreaFileResult.Invalid, result);
        Assert.True(area.IsFull);
    }

    [Fact]
    public void ThrowsWhenFileIsNotJson()
    {
        // Arrange
        Directory.CreateDirectory(directory);
        File.WriteAllText(AreaPath, "not json");

        // Assert
        Assert.ThrowsAny<JsonException>(() => DetectionAreaFile.Read(AreaPath, out _));
    }

    //--------------------------------------------------------------------------------
    // Write
    //--------------------------------------------------------------------------------

    [Fact]
    public void ThrowsWhenFileCannotBeWritten()
    {
        // Arrange (the folder of the file is a file)
        Directory.CreateDirectory(directory);
        var blocker = Path.Combine(directory, "blocker");
        File.WriteAllText(blocker, string.Empty);

        // Assert
        Assert.ThrowsAny<IOException>(() => DetectionAreaFile.Write(Path.Combine(blocker, "area.json"), DetectionArea.Full));
    }
}
