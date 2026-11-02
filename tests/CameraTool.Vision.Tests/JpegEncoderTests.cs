namespace CameraTool.Vision;

using System.Runtime.InteropServices.WindowsRuntime;

using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

public sealed class JpegEncoderTests
{
    private const int BytesPerPixel = 4;

    private const int Quality = 80;

    [Theory]
    [InlineData(16, 16)]
    [InlineData(64, 48)]
    [InlineData(33, 17)]
    [InlineData(640, 480)]
    [InlineData(1920, 1080)]
    public async Task EncodesNv12(int width, int height)
    {
        // Arrange
        var uvStride = ((width + 1) / 2) * 2;
        var (y, uv) = CreateGradient(width, height);
        var expected = new byte[width * height * BytesPerPixel];
        ImageConverter.Nv12ToBgra(y, width, uv, uvStride, expected, width * BytesPerPixel, width, height, YuvRange.Full);
        using var encoder = new JpegEncoder(Quality);
        var destination = new byte[JpegEncoder.GetMaxLength(width, height)];

        // Act
        var result = encoder.TryEncodeNv12(y, width, uv, uvStride, width, height, destination, out var written);

        // Assert
        Assert.True(result);
        Assert.Equal([0xFF, 0xD8], destination[..2]);
        Assert.Equal([0xFF, 0xD9], destination[(written - 2)..written]);

        var (decodedWidth, decodedHeight, actual) = await DecodeAsync(destination[..written]).ConfigureAwait(true);
        Assert.Equal(width, decodedWidth);
        Assert.Equal(height, decodedHeight);
        Assert.True(CalculatePsnr(expected, actual) > 30);
    }

    [Fact]
    public void ReturnsFalseWhenDestinationIsTooSmall()
    {
        // Arrange
        const int width = 640;
        const int height = 480;
        var (y, uv) = CreateGradient(width, height);
        using var encoder = new JpegEncoder(Quality);
        var destination = new byte[100];

        // Act
        var result = encoder.TryEncodeNv12(y, width, uv, width, width, height, destination, out var written);

        // Assert
        Assert.False(result);
        Assert.Equal(0, written);
    }

    [Fact]
    public void ThrowsWhenUvPlaneIsTooSmall()
    {
        using var encoder = new JpegEncoder(Quality);
        var y = new byte[16 * 16];
        var uv = new byte[(16 * 8) - 1];
        var destination = new byte[JpegEncoder.GetMaxLength(16, 16)];

        Assert.Throws<ArgumentException>(() => encoder.TryEncodeNv12(y, 16, uv, 16, 16, 16, destination, out _));
    }

    [Fact]
    public void ThrowsWhenDisposed()
    {
        var encoder = new JpegEncoder(Quality);
        encoder.Dispose();
        var y = new byte[16 * 16];
        var uv = new byte[16 * 8];
        var destination = new byte[JpegEncoder.GetMaxLength(16, 16)];

        Assert.Throws<ObjectDisposedException>(() => encoder.TryEncodeNv12(y, 16, uv, 16, 16, 16, destination, out _));
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static (byte[] Y, byte[] Uv) CreateGradient(int width, int height)
    {
        var uvStride = ((width + 1) / 2) * 2;
        var uvRows = (height + 1) / 2;
        var y = new byte[width * height];
        var uv = new byte[uvStride * uvRows];
        for (var row = 0; row < height; row++)
        {
            for (var x = 0; x < width; x++)
            {
                y[(row * width) + x] = (byte)(32 + ((x * 160) / width) + ((row * 32) / height));
            }
        }

        for (var row = 0; row < uvRows; row++)
        {
            for (var x = 0; x < uvStride; x += 2)
            {
                uv[(row * uvStride) + x] = (byte)(80 + ((row * 96) / uvRows));
                uv[(row * uvStride) + x + 1] = (byte)(176 - ((x * 96) / uvStride));
            }
        }

        return (y, uv);
    }

    private static async Task<(int Width, int Height, byte[] Bgra)> DecodeAsync(byte[] jpeg)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(jpeg.AsBuffer()).AsTask(cancellationToken).ConfigureAwait(true);
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(true);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore).AsTask(cancellationToken).ConfigureAwait(true);
        var bgra = new byte[bitmap.PixelWidth * bitmap.PixelHeight * BytesPerPixel];
        bitmap.CopyToBuffer(bgra.AsBuffer());
        return (bitmap.PixelWidth, bitmap.PixelHeight, bgra);
    }

    // RGB channels only
    private static double CalculatePsnr(byte[] expected, byte[] actual)
    {
        double sum = 0;
        var count = 0;
        for (var i = 0; i < expected.Length; i += BytesPerPixel)
        {
            for (var c = 0; c < 3; c++)
            {
                var difference = expected[i + c] - actual[i + c];
                sum += difference * difference;
                count++;
            }
        }

        var mse = sum / count;
        return mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse);
    }
}
