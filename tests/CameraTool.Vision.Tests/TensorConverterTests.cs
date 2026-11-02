namespace CameraTool.Vision;

using System.Runtime.Intrinsics.X86;

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
    // SIMD
    //--------------------------------------------------------------------------------

    [Theory]
    [InlineData(640, 480, 320, 240, 0)]
    [InlineData(1280, 720, 320, 240, 16)]
    [InlineData(1920, 1080, 320, 240, 0)]
    [InlineData(33, 17, 20, 9, 3)]
    [InlineData(200, 100, 130, 70, 5)]
    [InlineData(64, 48, 100, 80, 0)]
    public void BgraSimdMatchesScalar(int width, int height, int destinationWidth, int destinationHeight, int padding)
    {
        Assert.SkipUnless(Avx2.IsSupported, "AVX2 is not supported.");

        // Arrange
        var stride = (width * 4) + padding;
        var bgra = TestData.CreateRandom(stride * height, 1);
        var expected = new float[3 * destinationWidth * destinationHeight];
        var actual = new float[expected.Length];

        // Act
        TensorConverter.BgraToTensor(bgra, stride, width, height, expected, destinationWidth, destinationHeight, false);
        TensorConverter.BgraToTensor(bgra, stride, width, height, actual, destinationWidth, destinationHeight, true);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(640, 480, 320, 240, 0)]
    [InlineData(1280, 720, 320, 240, 16)]
    [InlineData(1920, 1080, 320, 240, 0)]
    [InlineData(33, 17, 20, 9, 3)]
    [InlineData(200, 100, 130, 70, 5)]
    [InlineData(64, 48, 100, 80, 0)]
    public void Nv12SimdMatchesScalar(int width, int height, int destinationWidth, int destinationHeight, int padding)
    {
        Assert.SkipUnless(Avx2.IsSupported, "AVX2 is not supported.");

        // Arrange
        var yStride = width + padding;
        var uvStride = (((width + 1) / 2) * 2) + padding;
        var y = TestData.CreateRandom(yStride * height, 2);
        var uv = TestData.CreateRandom(uvStride * ((height + 1) / 2), 3);

        foreach (var range in Enum.GetValues<YuvRange>())
        {
            var expected = new float[3 * destinationWidth * destinationHeight];
            var actual = new float[expected.Length];

            // Act
            TensorConverter.Nv12ToTensor(y, yStride, uv, uvStride, width, height, range, expected, destinationWidth, destinationHeight, false);
            TensorConverter.Nv12ToTensor(y, yStride, uv, uvStride, width, height, range, actual, destinationWidth, destinationHeight, true);

            // Assert
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData(640, 480, 320, 240, 0)]
    [InlineData(320, 240, 320, 240, 8)]
    [InlineData(33, 17, 20, 9, 3)]
    [InlineData(200, 100, 130, 70, 5)]
    [InlineData(64, 48, 100, 80, 0)]
    public void Yuy2SimdMatchesScalar(int width, int height, int destinationWidth, int destinationHeight, int padding)
    {
        Assert.SkipUnless(Avx2.IsSupported, "AVX2 is not supported.");

        // Arrange
        var stride = (((width + 1) / 2) * 4) + padding;
        var source = TestData.CreateRandom(stride * height, 4);

        foreach (var range in Enum.GetValues<YuvRange>())
        {
            var expected = new float[3 * destinationWidth * destinationHeight];
            var actual = new float[expected.Length];

            // Act
            TensorConverter.Yuy2ToTensor(source, stride, width, height, range, expected, destinationWidth, destinationHeight, false);
            TensorConverter.Yuy2ToTensor(source, stride, width, height, range, actual, destinationWidth, destinationHeight, true);

            // Assert
            Assert.Equal(expected, actual);
        }
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
