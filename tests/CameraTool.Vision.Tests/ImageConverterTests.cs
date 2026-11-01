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

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    // Deterministic xorshift data
    private static byte[] CreateRandom(int length, uint seed)
    {
        var buffer = new byte[length];
        var state = seed;
        for (var i = 0; i < buffer.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            buffer[i] = (byte)state;
        }

        return buffer;
    }

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
