namespace CameraTool.Vision;

public sealed class DetectionAreaTests
{
    private const int LastColumn = DetectionArea.Columns - 1;

    private const int LastRow = DetectionArea.Rows - 1;

    //--------------------------------------------------------------------------------
    // Cell
    //--------------------------------------------------------------------------------

    [Fact]
    public void FullContainsAllCells()
    {
        // Act
        var area = DetectionArea.Full;

        // Assert
        Assert.True(area.IsFull);
        Assert.Equal(MotionDetector.CellCount, area.CellCount);
        Assert.True(area.Contains(0, 0));
        Assert.True(area.Contains(LastColumn, LastRow));
    }

    [Fact]
    public void WithCellsRemovesRectangle()
    {
        // Act (the corners in reverse order)
        var area = DetectionArea.Full.WithCells(5, 4, 2, 1, false);

        // Assert
        Assert.False(area.IsFull);
        Assert.Equal(MotionDetector.CellCount - 16, area.CellCount);
        for (var row = 0; row < DetectionArea.Rows; row++)
        {
            for (var column = 0; column < DetectionArea.Columns; column++)
            {
                var removed = (column is >= 2 and <= 5) && (row is >= 1 and <= 4);
                Assert.Equal(!removed, area.Contains(column, row));
            }
        }

        Assert.True(DetectionArea.Full.IsFull);
    }

    [Fact]
    public void WithCellsAddsCells()
    {
        // Arrange
        var empty = DetectionArea.Full.WithCells(0, 0, LastColumn, LastRow, false);

        // Act
        var area = empty.WithCells(3, 7, 3, 7, true);

        // Assert
        Assert.Equal(0, empty.CellCount);
        Assert.Equal(1, area.CellCount);
        Assert.True(area.Contains(3, 7));
    }

    [Fact]
    public void ThrowsWhenCellIsOutside()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DetectionArea.Full.Contains(DetectionArea.Columns, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DetectionArea.Full.WithCells(0, -1, 0, 0, false));
    }

    [Theory]
    [InlineData(0f, 0f, 0, 0)]
    [InlineData(0.5f, 0.5f, 16, 12)]
    [InlineData(0.999f, 0.999f, LastColumn, LastRow)]
    [InlineData(1f, 1f, LastColumn, LastRow)]
    [InlineData(-0.5f, 2f, 0, LastRow)]
    public void ContainsPointMapsToCell(float x, float y, int column, int row)
    {
        // Arrange (only the cell is in the area)
        var area = DetectionArea.Full.WithCells(0, 0, LastColumn, LastRow, false).WithCells(column, row, column, row, true);

        // Act
        var contains = area.ContainsPoint(x, y);
        var containsNeighbor = area.ContainsPoint(x - (1.5f / DetectionArea.Columns), y);

        // Assert
        Assert.True(contains);
        Assert.Equal(column == 0, containsNeighbor);
    }

    //--------------------------------------------------------------------------------
    // Face
    //--------------------------------------------------------------------------------

    [Fact]
    public void FilterFacesKeepsFacesInArea()
    {
        // Arrange (the left half)
        var area = DetectionArea.Full.WithCells(16, 0, LastColumn, LastRow, false);
        var faces = new[]
        {
            new FaceBox(0.1f, 0.1f, 0.3f, 0.3f, 0.9f),
            new FaceBox(0.6f, 0.1f, 0.8f, 0.3f, 0.8f),
            new FaceBox(0.4f, 0.5f, 0.58f, 0.7f, 0.7f),
            new FaceBox(0.45f, 0.5f, 0.65f, 0.7f, 0.6f)
        };

        // Act
        var count = area.FilterFaces(faces);

        // Assert (the center x of the faces: 0.2, 0.7, 0.49, 0.55)
        Assert.Equal(2, count);
        Assert.Equal(0.9f, faces[0].Confidence);
        Assert.Equal(0.7f, faces[1].Confidence);
    }

    [Fact]
    public void FilterObjectsKeepsObjectsInArea()
    {
        // Arrange (the top half)
        var area = DetectionArea.Full.WithCells(0, 12, LastColumn, LastRow, false);
        var objects = new[]
        {
            new ObjectBox(0.1f, 0.1f, 0.3f, 0.6f, 0.9f, 0),
            new ObjectBox(0.1f, 0.5f, 0.3f, 0.9f, 0.8f, 16),
            new ObjectBox(0.6f, 0f, 0.9f, 0.4f, 0.7f, 2)
        };

        // Act
        var count = area.FilterObjects(objects);

        // Assert (the center y of the objects: 0.35, 0.7, 0.2)
        Assert.Equal(2, count);
        Assert.Equal(0, objects[0].ClassId);
        Assert.Equal(2, objects[1].ClassId);
    }

    [Fact]
    public void FullAreaKeepsAllFaces()
    {
        // Arrange
        var faces = new[]
        {
            new FaceBox(0.1f, 0.1f, 0.3f, 0.3f, 0.9f),
            new FaceBox(0.9f, 0.9f, 1.1f, 1.1f, 0.8f)
        };

        // Act
        var count = DetectionArea.Full.FilterFaces(faces);

        // Assert
        Assert.Equal(2, count);
    }

    //--------------------------------------------------------------------------------
    // Text
    //--------------------------------------------------------------------------------

    [Fact]
    public void RowsRoundTrip()
    {
        // Arrange
        var area = DetectionArea.Full.WithCells(0, 0, 7, LastRow, false).WithCells(20, 5, 25, 10, false);

        // Act
        var rows = area.ToRows();

        // Assert
        Assert.Equal(DetectionArea.Rows, rows.Length);
        Assert.All(rows, x => Assert.Equal(DetectionArea.Columns, x.Length));
        Assert.Equal("00000000111111111111111111111111", rows[0]);
        Assert.Equal("00000000111111111111000000111111", rows[5]);
        Assert.True(DetectionArea.TryParse(rows, out var result));
        Assert.True(result.Mask.SequenceEqual(area.Mask));
        Assert.Equal(area.CellCount, result.CellCount);
    }

    [Fact]
    public void TryParseRejectsInvalidRows()
    {
        // Arrange
        var rows = DetectionArea.Full.ToRows();

        // Assert
        Assert.False(DetectionArea.TryParse(null, out _));
        Assert.False(DetectionArea.TryParse(rows[..LastRow], out _));
        Assert.False(DetectionArea.TryParse(Replace(rows, 3, new string('1', DetectionArea.Columns - 1)), out _));
        Assert.False(DetectionArea.TryParse(Replace(rows, 3, "2" + new string('1', DetectionArea.Columns - 1)), out _));
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static string[] Replace(string[] rows, int index, string value)
    {
        var result = (string[])rows.Clone();
        result[index] = value;
        return result;
    }
}
