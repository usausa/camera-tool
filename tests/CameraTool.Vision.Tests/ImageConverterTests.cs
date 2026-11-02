namespace CameraTool.Vision;

using System.Runtime.Intrinsics.X86;

public sealed class ImageConverterTests
{
    private const int BytesPerPixel = 4;

    //--------------------------------------------------------------------------------
    // NV12
    //--------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 2, 0)]
    [InlineData(8, 2, 0)]
    [InlineData(18, 4, 0)]
    [InlineData(26, 6, 7)]
    [InlineData(33, 5, 3)]
    [InlineData(640, 480, 0)]
    [InlineData(642, 7, 32)]
    public void Nv12SimdMatchesScalar(int width, int height, int padding)
    {
        Assert.SkipUnless(Avx2.IsSupported, "AVX2 is not supported.");

        // Arrange
        var yStride = width + padding;
        var uvStride = (((width + 1) / 2) * 2) + padding;
        var bgraStride = (width * BytesPerPixel) + padding;
        var y = CreateRandom(yStride * height, 1);
        var uv = CreateRandom(uvStride * ((height + 1) / 2), 2);

        foreach (var range in Enum.GetValues<YuvRange>())
        {
            var expected = new byte[bgraStride * height];
            var actual = new byte[bgraStride * height];

            // Act
            ImageConverter.Nv12ToBgra(y, yStride, uv, uvStride, expected, bgraStride, width, height, range, false);
            ImageConverter.Nv12ToBgra(y, yStride, uv, uvStride, actual, bgraStride, width, height, range, true);

            // Assert
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData(YuvRange.Limited, 16, 128, 128, 0, 0, 0)]
    [InlineData(YuvRange.Limited, 235, 128, 128, 255, 255, 255)]
    [InlineData(YuvRange.Limited, 81, 90, 240, 255, 0, 0)]
    [InlineData(YuvRange.Limited, 145, 54, 34, 0, 255, 1)]
    [InlineData(YuvRange.Limited, 41, 240, 110, 0, 0, 255)]
    [InlineData(YuvRange.Full, 0, 128, 128, 0, 0, 0)]
    [InlineData(YuvRange.Full, 255, 128, 128, 255, 255, 255)]
    [InlineData(YuvRange.Full, 76, 85, 255, 254, 0, 0)]
    public void Nv12ConvertsKnownColor(YuvRange range, byte y, byte u, byte v, byte r, byte g, byte b)
    {
        // Arrange
        const int width = 16;
        const int height = 2;
        var yPlane = Enumerable.Repeat(y, width * height).ToArray();
        var uvPlane = new byte[width];
        for (var i = 0; i < uvPlane.Length; i += 2)
        {
            uvPlane[i] = u;
            uvPlane[i + 1] = v;
        }

        var bgra = new byte[width * height * BytesPerPixel];

        // Act
        ImageConverter.Nv12ToBgra(yPlane, width, uvPlane, width, bgra, width * BytesPerPixel, width, height, range);

        // Assert
        AssertColor(bgra, r, g, b);
    }

    //--------------------------------------------------------------------------------
    // YUY2
    //--------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 2, 0)]
    [InlineData(8, 2, 0)]
    [InlineData(18, 4, 0)]
    [InlineData(26, 6, 7)]
    [InlineData(33, 5, 3)]
    [InlineData(640, 480, 0)]
    [InlineData(642, 7, 32)]
    public void Yuy2SimdMatchesScalar(int width, int height, int padding)
    {
        Assert.SkipUnless(Avx2.IsSupported, "AVX2 is not supported.");

        // Arrange
        var stride = (((width + 1) / 2) * 4) + padding;
        var bgraStride = (width * BytesPerPixel) + padding;
        var source = CreateRandom(stride * height, 3);

        foreach (var range in Enum.GetValues<YuvRange>())
        {
            var expected = new byte[bgraStride * height];
            var actual = new byte[bgraStride * height];

            // Act
            ImageConverter.Yuy2ToBgra(source, stride, expected, bgraStride, width, height, range, false);
            ImageConverter.Yuy2ToBgra(source, stride, actual, bgraStride, width, height, range, true);

            // Assert
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData(YuvRange.Limited, 16, 128, 128, 0, 0, 0)]
    [InlineData(YuvRange.Limited, 235, 128, 128, 255, 255, 255)]
    [InlineData(YuvRange.Limited, 81, 90, 240, 255, 0, 0)]
    [InlineData(YuvRange.Limited, 145, 54, 34, 0, 255, 1)]
    [InlineData(YuvRange.Limited, 41, 240, 110, 0, 0, 255)]
    [InlineData(YuvRange.Full, 0, 128, 128, 0, 0, 0)]
    [InlineData(YuvRange.Full, 255, 128, 128, 255, 255, 255)]
    [InlineData(YuvRange.Full, 76, 85, 255, 254, 0, 0)]
    public void Yuy2ConvertsKnownColor(YuvRange range, byte y, byte u, byte v, byte r, byte g, byte b)
    {
        // Arrange
        const int width = 16;
        const int height = 2;
        var source = new byte[width * 2 * height];
        for (var i = 0; i < source.Length; i += 4)
        {
            source[i] = y;
            source[i + 1] = u;
            source[i + 2] = y;
            source[i + 3] = v;
        }

        var bgra = new byte[width * height * BytesPerPixel];

        // Act
        ImageConverter.Yuy2ToBgra(source, width * 2, bgra, width * BytesPerPixel, width, height, range);

        // Assert
        AssertColor(bgra, r, g, b);
    }

    //--------------------------------------------------------------------------------
    // Full range NV12
    //--------------------------------------------------------------------------------

    [Theory]
    [InlineData(2, 1, 0)]
    [InlineData(2, 2, 0)]
    [InlineData(16, 2, 0)]
    [InlineData(18, 3, 0)]
    [InlineData(33, 5, 3)]
    [InlineData(48, 4, 8)]
    [InlineData(640, 480, 0)]
    [InlineData(642, 7, 32)]
    public void Yuy2ToNv12SimdMatchesScalar(int width, int height, int padding)
    {
        Assert.SkipUnless(Ssse3.IsSupported, "SSSE3 is not supported.");

        // Arrange
        var sourceStride = (((width + 1) / 2) * 4) + padding;
        var yStride = width + padding;
        var uvStride = (((width + 1) / 2) * 2) + padding;
        var source = CreateRandom(sourceStride * height, 3);

        foreach (var range in Enum.GetValues<YuvRange>())
        {
            var expectedY = new byte[yStride * height];
            var expectedUv = new byte[uvStride * ((height + 1) / 2)];
            var actualY = new byte[expectedY.Length];
            var actualUv = new byte[expectedUv.Length];

            // Act
            ImageConverter.Yuy2ToNv12(source, sourceStride, expectedY, yStride, expectedUv, uvStride, width, height, range, false);
            ImageConverter.Yuy2ToNv12(source, sourceStride, actualY, yStride, actualUv, uvStride, width, height, range, true);

            // Assert
            Assert.Equal(expectedY, actualY);
            Assert.Equal(expectedUv, actualUv);
        }
    }

    [Theory]
    [InlineData(16, 128, 0, 128)]
    [InlineData(235, 16, 255, 1)]
    [InlineData(126, 240, 128, 255)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(255, 255, 255, 255)]
    public void Yuy2ToNv12ExpandsLimitedRange(byte y, byte chroma, byte expectedY, byte expectedChroma)
    {
        // Arrange
        const int width = 32;
        const int height = 2;
        var source = Enumerable.Range(0, width * 2 * height).Select(i => (i & 1) == 0 ? y : chroma).ToArray();
        var yPlane = new byte[width * height];
        var uvPlane = new byte[width];

        // Act
        ImageConverter.Yuy2ToNv12(source, width * 2, yPlane, width, uvPlane, width, width, height, YuvRange.Limited);

        // Assert
        Assert.All(yPlane, x => Assert.Equal(expectedY, x));
        Assert.All(uvPlane, x => Assert.Equal(expectedChroma, x));
    }

    [Theory]
    [InlineData(16, 240)]
    [InlineData(235, 16)]
    public void Yuy2ToNv12KeepsRangeOfSource(byte y, byte chroma)
    {
        // Arrange
        const int width = 32;
        const int height = 2;
        var source = Enumerable.Range(0, width * 2 * height).Select(i => (i & 1) == 0 ? y : chroma).ToArray();
        var yPlane = new byte[width * height];
        var uvPlane = new byte[width];

        // Act
        ImageConverter.Yuy2ToNv12(source, width * 2, yPlane, width, uvPlane, width, width, height);

        // Assert
        Assert.All(yPlane, x => Assert.Equal(y, x));
        Assert.All(uvPlane, x => Assert.Equal(chroma, x));
    }

    [Fact]
    public void Yuy2ToNv12AveragesChromaOfTwoRows()
    {
        // Arrange
        const int width = 32;
        const int height = 2;
        var source = new byte[width * 2 * height];
        for (var row = 0; row < height; row++)
        {
            for (var x = 0; x < width; x += 2)
            {
                var offset = (row * width * 2) + (x * 2);
                source[offset] = (byte)x;
                source[offset + 1] = row == 0 ? (byte)100 : (byte)201;
                source[offset + 2] = (byte)(x + 1);
                source[offset + 3] = row == 0 ? (byte)50 : (byte)60;
            }
        }

        var yPlane = new byte[width * height];
        var uvPlane = new byte[width];

        // Act
        ImageConverter.Yuy2ToNv12(source, width * 2, yPlane, width, uvPlane, width, width, height, YuvRange.Full);

        // Assert
        for (var x = 0; x < width; x++)
        {
            Assert.Equal(x, yPlane[x]);
            Assert.Equal(x, yPlane[width + x]);
        }

        for (var x = 0; x < width; x += 2)
        {
            Assert.Equal(151, uvPlane[x]);
            Assert.Equal(55, uvPlane[x + 1]);
        }
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(16, 2, 0)]
    [InlineData(33, 5, 3)]
    [InlineData(640, 480, 0)]
    [InlineData(642, 7, 32)]
    public void Nv12ToFullRangeSimdMatchesScalar(int width, int height, int padding)
    {
        Assert.SkipUnless(Ssse3.IsSupported, "SSSE3 is not supported.");

        // Arrange
        var yStride = width + padding;
        var uvStride = (((width + 1) / 2) * 2) + padding;
        var uvLength = uvStride * ((height + 1) / 2);
        var y = CreateRandom(yStride * height, 4);
        var uv = CreateRandom(uvLength, 5);
        var expectedY = new byte[y.Length];
        var expectedUv = new byte[uvLength];
        var actualY = new byte[y.Length];
        var actualUv = new byte[uvLength];

        // Act
        ImageConverter.Nv12ToFullRange(y, yStride, uv, uvStride, expectedY, yStride, expectedUv, uvStride, width, height, false);
        ImageConverter.Nv12ToFullRange(y, yStride, uv, uvStride, actualY, yStride, actualUv, uvStride, width, height, true);

        // Assert
        Assert.Equal(expectedY, actualY);
        Assert.Equal(expectedUv, actualUv);
    }

    [Theory]
    [InlineData(16, 128, 0, 128)]
    [InlineData(235, 16, 255, 1)]
    [InlineData(126, 240, 128, 255)]
    public void Nv12ToFullRangeExpandsKnownValue(byte y, byte chroma, byte expectedY, byte expectedChroma)
    {
        // Arrange
        const int width = 32;
        const int height = 2;
        var yPlane = Enumerable.Repeat(y, width * height).ToArray();
        var uvPlane = Enumerable.Repeat(chroma, width).ToArray();
        var destinationY = new byte[yPlane.Length];
        var destinationUv = new byte[uvPlane.Length];

        // Act
        ImageConverter.Nv12ToFullRange(yPlane, width, uvPlane, width, destinationY, width, destinationUv, width, width, height);

        // Assert
        Assert.All(destinationY, x => Assert.Equal(expectedY, x));
        Assert.All(destinationUv, x => Assert.Equal(expectedChroma, x));
    }

    //--------------------------------------------------------------------------------
    // Validation
    //--------------------------------------------------------------------------------

    [Fact]
    public void Nv12ThrowsWhenDestinationIsTooSmall()
    {
        var y = new byte[16 * 2];
        var uv = new byte[16];
        var bgra = new byte[(16 * 2 * BytesPerPixel) - 1];

        Assert.Throws<ArgumentException>(() => ImageConverter.Nv12ToBgra(y, 16, uv, 16, bgra, 16 * BytesPerPixel, 16, 2, YuvRange.Limited));
    }

    [Fact]
    public void Yuy2ThrowsWhenSourceIsTooSmall()
    {
        var source = new byte[(16 * 2 * 2) - 1];
        var bgra = new byte[16 * 2 * BytesPerPixel];

        Assert.Throws<ArgumentException>(() => ImageConverter.Yuy2ToBgra(source, 16 * 2, bgra, 16 * BytesPerPixel, 16, 2, YuvRange.Limited));
    }

    [Fact]
    public void Yuy2ToNv12ThrowsWhenUvPlaneIsTooSmall()
    {
        var source = new byte[16 * 2 * 2];
        var y = new byte[16 * 2];
        var uv = new byte[16 - 1];

        Assert.Throws<ArgumentException>(() => ImageConverter.Yuy2ToNv12(source, 16 * 2, y, 16, uv, 16, 16, 2, YuvRange.Limited));
    }

    [Fact]
    public void Nv12ToFullRangeThrowsWhenDestinationIsTooSmall()
    {
        var y = new byte[16 * 2];
        var uv = new byte[16];
        var destinationY = new byte[(16 * 2) - 1];
        var destinationUv = new byte[16];

        Assert.Throws<ArgumentException>(() => ImageConverter.Nv12ToFullRange(y, 16, uv, 16, destinationY, 16, destinationUv, 16, 16, 2));
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static byte[] CreateRandom(int length, uint seed) => TestData.CreateRandom(length, seed);

    private static void AssertColor(byte[] bgra, byte r, byte g, byte b)
    {
        for (var i = 0; i < bgra.Length; i += BytesPerPixel)
        {
            Assert.Equal(b, bgra[i]);
            Assert.Equal(g, bgra[i + 1]);
            Assert.Equal(r, bgra[i + 2]);
            Assert.Equal(255, bgra[i + 3]);
        }
    }
}
