namespace CameraTool.Vision;

public sealed class TensorConverterTests
{
    private const double Tolerance = 1e-5;

    private const float Black = -127f / 128f;

    //--------------------------------------------------------------------------------
    // BGRA
    //--------------------------------------------------------------------------------

    [Fact]
    public void BgraToTensorWritesRgbPlanes()
    {
        // Arrange
        var bgra = new byte[4 * 4 * 4];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            bgra[i + 2] = 255;
            bgra[i + 3] = 255;
        }

        var tensor = new float[3 * 2 * 2];

        // Act
        TensorConverter.BgraToTensor(bgra, 4 * 4, 4, 4, tensor, 2, 2);

        // Assert
        AssertPlane(tensor, 0, 4, 1f);
        AssertPlane(tensor, 1, 4, Black);
        AssertPlane(tensor, 2, 4, Black);
    }

    [Fact]
    public void BgraToTensorAveragesOnHalfScale()
    {
        // Arrange
        var bgra = new byte[2 * 2 * 4];
        bgra[2] = 0;
        bgra[6] = 100;
        bgra[10] = 200;
        bgra[14] = 255;
        var tensor = new float[3];

        // Act
        TensorConverter.BgraToTensor(bgra, 2 * 4, 2, 2, tensor, 1, 1);

        // Assert
        Assert.Equal((138.75 - 127) / 128, tensor[0], Tolerance);
    }

    //--------------------------------------------------------------------------------
    // YUV
    //--------------------------------------------------------------------------------

    [Theory]
    [InlineData(YuvRange.Full, 127, 0f)]
    [InlineData(YuvRange.Limited, 16, Black)]
    [InlineData(YuvRange.Limited, 235, 1f)]
    public void Nv12ToTensorConvertsGray(YuvRange range, byte y, float expected)
    {
        // Arrange
        var yPlane = Enumerable.Repeat(y, 4 * 4).ToArray();
        var uvPlane = Enumerable.Repeat((byte)128, 4 * 2).ToArray();
        var tensor = new float[3 * 2 * 2];

        // Act
        TensorConverter.Nv12ToTensor(yPlane, 4, uvPlane, 4, 4, 4, range, tensor, 2, 2);

        // Assert
        AssertPlane(tensor, 0, 4, expected);
        AssertPlane(tensor, 1, 4, expected);
        AssertPlane(tensor, 2, 4, expected);
    }

    [Theory]
    [InlineData(YuvRange.Full, 127, 0f)]
    [InlineData(YuvRange.Limited, 16, Black)]
    [InlineData(YuvRange.Limited, 235, 1f)]
    public void Yuy2ToTensorConvertsGray(YuvRange range, byte y, float expected)
    {
        // Arrange
        var source = new byte[4 * 2 * 4];
        for (var i = 0; i < source.Length; i += 4)
        {
            source[i] = y;
            source[i + 1] = 128;
            source[i + 2] = y;
            source[i + 3] = 128;
        }

        var tensor = new float[3 * 2 * 2];

        // Act
        TensorConverter.Yuy2ToTensor(source, 4 * 2, 4, 4, range, tensor, 2, 2);

        // Assert
        AssertPlane(tensor, 0, 4, expected);
        AssertPlane(tensor, 1, 4, expected);
        AssertPlane(tensor, 2, 4, expected);
    }

    [Fact]
    public void Nv12ToTensorThrowsWhenDestinationIsTooSmall()
    {
        var y = new byte[4 * 4];
        var uv = new byte[4 * 2];
        var tensor = new float[(3 * 2 * 2) - 1];

        Assert.Throws<ArgumentException>(() => TensorConverter.Nv12ToTensor(y, 4, uv, 4, 4, 4, YuvRange.Limited, tensor, 2, 2));
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static void AssertPlane(float[] tensor, int plane, int planeSize, float expected)
    {
        for (var i = 0; i < planeSize; i++)
        {
            Assert.Equal(expected, tensor[(plane * planeSize) + i], Tolerance);
        }
    }
}
