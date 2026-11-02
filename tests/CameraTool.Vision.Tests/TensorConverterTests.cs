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
        TensorConverter.BgraToTensor(bgra, 4 * 4, 4, 4, tensor, 2, 2, TensorNormalization.Centered);

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
        TensorConverter.BgraToTensor(bgra, 2 * 4, 2, 2, tensor, 1, 1, TensorNormalization.Centered);

        // Assert
        Assert.Equal((138.75 - 127) / 128, tensor[0], Tolerance);
    }

    [Fact]
    public void BgraToTensorNormalizesToUnitRange()
    {
        // Arrange (B = 0, G = 51, R = 255)
        var bgra = new byte[2 * 2 * 4];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            bgra[i + 1] = 51;
            bgra[i + 2] = 255;
            bgra[i + 3] = 255;
        }

        var tensor = new float[3];

        // Act
        TensorConverter.BgraToTensor(bgra, 2 * 4, 2, 2, tensor, 1, 1, TensorNormalization.Unit);

        // Assert
        Assert.Equal(1f, tensor[0], Tolerance);
        Assert.Equal(0.2f, tensor[1], Tolerance);
        Assert.Equal(0f, tensor[2], Tolerance);
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
        TensorConverter.Nv12ToTensor(yPlane, 4, uvPlane, 4, 4, 4, range, tensor, 2, 2, TensorNormalization.Centered);

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
        TensorConverter.Yuy2ToTensor(source, 4 * 2, 4, 4, range, tensor, 2, 2, TensorNormalization.Centered);

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

        Assert.Throws<ArgumentException>(() => TensorConverter.Nv12ToTensor(y, 4, uv, 4, 4, 4, YuvRange.Limited, tensor, 2, 2, TensorNormalization.Centered));
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
        TensorConverter.BgraToTensor(bgra, stride, width, height, expected, destinationWidth, destinationHeight, TensorNormalization.Centered, false);
        TensorConverter.BgraToTensor(bgra, stride, width, height, actual, destinationWidth, destinationHeight, TensorNormalization.Centered, true);

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
            TensorConverter.Nv12ToTensor(y, yStride, uv, uvStride, width, height, range, expected, destinationWidth, destinationHeight, TensorNormalization.Centered, false);
            TensorConverter.Nv12ToTensor(y, yStride, uv, uvStride, width, height, range, actual, destinationWidth, destinationHeight, TensorNormalization.Centered, true);

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
            TensorConverter.Yuy2ToTensor(source, stride, width, height, range, expected, destinationWidth, destinationHeight, TensorNormalization.Centered, false);
            TensorConverter.Yuy2ToTensor(source, stride, width, height, range, actual, destinationWidth, destinationHeight, TensorNormalization.Centered, true);

            // Assert
            Assert.Equal(expected, actual);
        }
    }

    // Object detection input (upscaled to 640x640)
    [Theory]
    [InlineData(640, 480)]
    [InlineData(1280, 720)]
    public void SimdMatchesScalarWithUnitNormalization(int width, int height)
    {
        Assert.SkipUnless(Avx2.IsSupported, "AVX2 is not supported.");

        // Arrange
        const int size = 640;
        var y = TestData.CreateRandom(width * height, 7);
        var uv = TestData.CreateRandom(width * ((height + 1) / 2), 8);
        var yuy2 = TestData.CreateRandom(width * 2 * height, 9);
        var expectedNv12 = new float[3 * size * size];
        var actualNv12 = new float[expectedNv12.Length];
        var expectedYuy2 = new float[expectedNv12.Length];
        var actualYuy2 = new float[expectedNv12.Length];

        // Act
        TensorConverter.Nv12ToTensor(y, width, uv, width, width, height, YuvRange.Limited, expectedNv12, size, size, TensorNormalization.Unit, false);
        TensorConverter.Nv12ToTensor(y, width, uv, width, width, height, YuvRange.Limited, actualNv12, size, size, TensorNormalization.Unit, true);
        TensorConverter.Yuy2ToTensor(yuy2, width * 2, width, height, YuvRange.Limited, expectedYuy2, size, size, TensorNormalization.Unit, false);
        TensorConverter.Yuy2ToTensor(yuy2, width * 2, width, height, YuvRange.Limited, actualYuy2, size, size, TensorNormalization.Unit, true);

        // Assert
        Assert.Equal(expectedNv12, actualNv12);
        Assert.Equal(expectedYuy2, actualYuy2);
        Assert.InRange(expectedNv12.Min(), 0f, 1f);
        Assert.InRange(expectedNv12.Max(), 0f, 1f);
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
