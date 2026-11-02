namespace CameraTool.Vision;

using System.Runtime.Intrinsics.X86;

public sealed class MotionDetectorTests
{
    private const int Width = 640;

    private const int Height = 480;

    // 640x480: one cell is 20x20 pixels
    private const int CellPixels = 20;

    private const int LastColumn = MotionDetector.Columns - 1;

    private const int LastRow = MotionDetector.Rows - 1;

    //--------------------------------------------------------------------------------
    // Detection
    //--------------------------------------------------------------------------------

    [Fact]
    public void FirstFrameInitializesBackground()
    {
        // Arrange
        var detector = new MotionDetector(new MotionDetectorOptions());
        var mask = new byte[MotionDetector.MaskLength];

        // Act
        var result = detector.DetectNv12(CreatePlane(100), Width, Width, Height, mask);

        // Assert
        Assert.Equal(default, result);
        Assert.All(mask, x => Assert.Equal(0, x));
    }

    [Fact]
    public void SameFrameHasNoMotion()
    {
        // Arrange
        var detector = new MotionDetector(new MotionDetectorOptions());
        var mask = new byte[MotionDetector.MaskLength];
        var frame = CreatePlane(100);
        detector.DetectNv12(frame, Width, Width, Height, mask);

        // Act
        var result = detector.DetectNv12(frame, Width, Width, Height, mask);

        // Assert
        Assert.False(result.Detected);
        Assert.Equal(0, result.Cells);
    }

    [Fact]
    public void DetectsChangedCells()
    {
        // Arrange
        var detector = new MotionDetector(new MotionDetectorOptions());
        var mask = new byte[MotionDetector.MaskLength];
        detector.DetectNv12(CreatePlane(100), Width, Width, Height, mask);
        var changed = CreatePlane(100);
        FillCells(changed, 8, 6, 4, 4, 200);

        // Act
        var result = detector.DetectNv12(changed, Width, Width, Height, mask);

        // Assert
        Assert.True(result.Detected);
        Assert.Equal(16, result.Cells);
        Assert.Equal(16f / MotionDetector.CellCount, result.Level);
        for (var row = 0; row < MotionDetector.Rows; row++)
        {
            for (var column = 0; column < MotionDetector.Columns; column++)
            {
                var expected = (column is >= 8 and < 12) && (row is >= 6 and < 10);
                Assert.Equal(expected, IsSet(mask, row, column));
            }
        }
    }

    [Fact]
    public void IgnoresSmallDifference()
    {
        // Arrange
        var detector = new MotionDetector(new MotionDetectorOptions { Threshold = 25 });
        var mask = new byte[MotionDetector.MaskLength];
        detector.DetectNv12(CreatePlane(100), Width, Width, Height, mask);

        // Act
        var result = detector.DetectNv12(CreatePlane(120), Width, Width, Height, mask);

        // Assert
        Assert.False(result.Detected);
        Assert.Equal(0, result.Cells);
    }

    [Fact]
    public void ReportsNoMotionBelowMinArea()
    {
        // Arrange
        var detector = new MotionDetector(new MotionDetectorOptions { MinArea = 0.005f });
        var mask = new byte[MotionDetector.MaskLength];
        detector.DetectNv12(CreatePlane(100), Width, Width, Height, mask);
        var changed = CreatePlane(100);
        FillCells(changed, 0, 0, 1, 1, 200);

        // Act
        var result = detector.DetectNv12(changed, Width, Width, Height, mask);

        // Assert
        Assert.False(result.Detected);
        Assert.Equal(1, result.Cells);
        Assert.True(IsSet(mask, 0, 0));
    }

    [Fact]
    public void ChangeBecomesBackground()
    {
        // Arrange
        var detector = new MotionDetector(new MotionDetectorOptions { BackgroundRate = 0.05f });
        var mask = new byte[MotionDetector.MaskLength];
        detector.DetectNv12(CreatePlane(100), Width, Width, Height, mask);
        var changed = CreatePlane(100);
        FillCells(changed, 8, 6, 4, 4, 200);

        // Act
        var first = detector.DetectNv12(changed, Width, Width, Height, mask);
        var last = first;
        for (var i = 0; i < 40; i++)
        {
            last = detector.DetectNv12(changed, Width, Width, Height, mask);
        }

        // Assert
        Assert.True(first.Detected);
        Assert.False(last.Detected);
    }

    [Fact]
    public void FormatsGiveSameResult()
    {
        // Arrange
        var previous = CreatePlane(100);
        var current = CreatePlane(100);
        FillCells(current, 3, 2, 5, 3, 180);
        var expected = new byte[MotionDetector.MaskLength];
        var nv12 = new MotionDetector(new MotionDetectorOptions());
        nv12.DetectNv12(previous, Width, Width, Height, expected);
        var expectedResult = nv12.DetectNv12(current, Width, Width, Height, expected);

        var yuy2 = new MotionDetector(new MotionDetectorOptions());
        var yuy2Mask = new byte[MotionDetector.MaskLength];
        yuy2.DetectYuy2(ToYuy2(previous), Width * 2, Width, Height, yuy2Mask);
        var yuy2Result = yuy2.DetectYuy2(ToYuy2(current), Width * 2, Width, Height, yuy2Mask);

        var bgra = new MotionDetector(new MotionDetectorOptions());
        var bgraMask = new byte[MotionDetector.MaskLength];
        bgra.DetectBgra(ToBgra(previous), Width * 4, Width, Height, bgraMask);
        var bgraResult = bgra.DetectBgra(ToBgra(current), Width * 4, Width, Height, bgraMask);

        // Assert
        Assert.Equal(15, expectedResult.Cells);
        Assert.Equal(expectedResult, yuy2Result);
        Assert.Equal(expected, yuy2Mask);
        Assert.Equal(expectedResult, bgraResult);
        Assert.Equal(expected, bgraMask);
    }

    //--------------------------------------------------------------------------------
    // Area
    //--------------------------------------------------------------------------------

    [Fact]
    public void IgnoresCellsOutsideArea()
    {
        // Arrange (the cells from column 10 are outside)
        var detector = new MotionDetector(new MotionDetectorOptions()) { Area = DetectionArea.Full.WithCells(10, 0, LastColumn, LastRow, false) };
        var mask = new byte[MotionDetector.MaskLength];
        detector.DetectNv12(CreatePlane(100), Width, Width, Height, mask);
        var changed = CreatePlane(100);
        FillCells(changed, 8, 6, 4, 4, 200);

        // Act
        var result = detector.DetectNv12(changed, Width, Width, Height, mask);

        // Assert
        Assert.True(result.Detected);
        Assert.Equal(8, result.Cells);
        for (var row = 0; row < MotionDetector.Rows; row++)
        {
            for (var column = 0; column < MotionDetector.Columns; column++)
            {
                var expected = (column is >= 8 and < 10) && (row is >= 6 and < 10);
                Assert.Equal(expected, IsSet(mask, row, column));
            }
        }
    }

    [Fact]
    public void LevelAndMinAreaAreRelativeToArea()
    {
        // Arrange (16 cells in the area, one moving cell is below MinArea of the full area)
        var area = DetectionArea.Full.WithCells(0, 0, LastColumn, LastRow, false).WithCells(0, 0, 3, 3, true);
        var detector = new MotionDetector(new MotionDetectorOptions { MinArea = 0.005f }) { Area = area };
        var mask = new byte[MotionDetector.MaskLength];
        detector.DetectNv12(CreatePlane(100), Width, Width, Height, mask);
        var changed = CreatePlane(100);
        FillCells(changed, 0, 0, 1, 1, 200);

        // Act
        var result = detector.DetectNv12(changed, Width, Width, Height, mask);

        // Assert
        Assert.True(result.Detected);
        Assert.Equal(1, result.Cells);
        Assert.Equal(1f / 16, result.Level);
    }

    [Fact]
    public void EmptyAreaHasNoMotion()
    {
        // Arrange
        var detector = new MotionDetector(new MotionDetectorOptions()) { Area = DetectionArea.Full.WithCells(0, 0, LastColumn, LastRow, false) };
        var mask = new byte[MotionDetector.MaskLength];
        detector.DetectNv12(CreatePlane(100), Width, Width, Height, mask);

        // Act
        var result = detector.DetectNv12(CreatePlane(200), Width, Width, Height, mask);

        // Assert
        Assert.Equal(default, result);
        Assert.All(mask, x => Assert.Equal(0, x));
    }

    [Fact]
    public void AreaChangeKeepsBackground()
    {
        // Arrange (the changed cells are outside the area)
        var detector = new MotionDetector(new MotionDetectorOptions()) { Area = DetectionArea.Full.WithCells(8, 6, 11, 9, false) };
        var mask = new byte[MotionDetector.MaskLength];
        detector.DetectNv12(CreatePlane(100), Width, Width, Height, mask);
        var changed = CreatePlane(100);
        FillCells(changed, 8, 6, 4, 4, 200);
        var outside = detector.DetectNv12(changed, Width, Width, Height, mask);

        // Act (the next frame is compared with the same background)
        detector.Area = DetectionArea.Full;
        var inside = detector.DetectNv12(changed, Width, Width, Height, mask);

        // Assert
        Assert.False(outside.Detected);
        Assert.Equal(0, outside.Cells);
        Assert.True(inside.Detected);
        Assert.Equal(16, inside.Cells);
    }

    [Fact]
    public void ParsedFullAreaGivesSameResult()
    {
        // Arrange
        Assert.True(DetectionArea.TryParse(DetectionArea.Full.ToRows(), out var full));
        var previous = CreatePlane(100);
        var current = CreatePlane(100);
        FillCells(current, 3, 2, 5, 3, 180);
        var expected = new byte[MotionDetector.MaskLength];
        var detector = new MotionDetector(new MotionDetectorOptions());
        detector.DetectNv12(previous, Width, Width, Height, expected);
        var expectedResult = detector.DetectNv12(current, Width, Width, Height, expected);
        var actual = new byte[MotionDetector.MaskLength];
        var parsed = new MotionDetector(new MotionDetectorOptions()) { Area = full };
        parsed.DetectNv12(previous, Width, Width, Height, actual);

        // Act
        var result = parsed.DetectNv12(current, Width, Width, Height, actual);

        // Assert
        Assert.Equal(expectedResult, result);
        Assert.Equal(expected, actual);
    }

    //--------------------------------------------------------------------------------
    // SIMD
    //--------------------------------------------------------------------------------

    [Theory]
    [InlineData(640, 480, 0)]
    [InlineData(1280, 720, 16)]
    [InlineData(1920, 1080, 0)]
    [InlineData(33, 17, 3)]
    [InlineData(200, 150, 5)]
    public void Nv12SimdMatchesScalar(int width, int height, int padding)
    {
        Assert.SkipUnless(Avx2.IsSupported, "AVX2 is not supported.");

        // Arrange
        var stride = width + padding;
        var source = TestData.CreateRandom(stride * height, 5);
        var scalar = new MotionDetector(new MotionDetectorOptions());
        var vector = new MotionDetector(new MotionDetectorOptions());
        var mask = new byte[MotionDetector.MaskLength];

        // Act
        scalar.DetectNv12(source, stride, width, height, mask, false);
        vector.DetectNv12(source, stride, width, height, mask, true);

        // Assert
        Assert.True(scalar.Luma.SequenceEqual(vector.Luma));
    }

    [Theory]
    [InlineData(640, 480, 0)]
    [InlineData(320, 240, 8)]
    [InlineData(33, 17, 3)]
    [InlineData(200, 150, 5)]
    public void Yuy2SimdMatchesScalar(int width, int height, int padding)
    {
        Assert.SkipUnless(Avx2.IsSupported, "AVX2 is not supported.");

        // Arrange
        var stride = (((width + 1) / 2) * 4) + padding;
        var source = TestData.CreateRandom(stride * height, 6);
        var scalar = new MotionDetector(new MotionDetectorOptions());
        var vector = new MotionDetector(new MotionDetectorOptions());
        var mask = new byte[MotionDetector.MaskLength];

        // Act
        scalar.DetectYuy2(source, stride, width, height, mask, false);
        vector.DetectYuy2(source, stride, width, height, mask, true);

        // Assert
        Assert.True(scalar.Luma.SequenceEqual(vector.Luma));
    }

    //--------------------------------------------------------------------------------
    // Validation
    //--------------------------------------------------------------------------------

    [Fact]
    public void ThrowsWhenMaskIsTooSmall()
    {
        var detector = new MotionDetector(new MotionDetectorOptions());
        var mask = new byte[MotionDetector.MaskLength - 1];

        Assert.Throws<ArgumentException>(() => detector.DetectNv12(CreatePlane(100), Width, Width, Height, mask));
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static byte[] CreatePlane(byte value) => Enumerable.Repeat(value, Width * Height).ToArray();

    private static void FillCells(byte[] plane, int column, int row, int columns, int rows, byte value)
    {
        for (var y = row * CellPixels; y < (row + rows) * CellPixels; y++)
        {
            plane.AsSpan((y * Width) + (column * CellPixels), columns * CellPixels).Fill(value);
        }
    }

    private static bool IsSet(byte[] mask, int row, int column)
    {
        var index = (row * MotionDetector.Columns) + column;
        return (mask[index >> 3] & (1 << (index & 7))) != 0;
    }

    private static byte[] ToYuy2(byte[] plane)
    {
        var yuy2 = new byte[plane.Length * 2];
        for (var i = 0; i < plane.Length; i++)
        {
            yuy2[i * 2] = plane[i];
            yuy2[(i * 2) + 1] = 128;
        }

        return yuy2;
    }

    private static byte[] ToBgra(byte[] plane)
    {
        var bgra = new byte[plane.Length * 4];
        for (var i = 0; i < plane.Length; i++)
        {
            bgra[i * 4] = plane[i];
            bgra[(i * 4) + 1] = plane[i];
            bgra[(i * 4) + 2] = plane[i];
            bgra[(i * 4) + 3] = 255;
        }

        return bgra;
    }
}
