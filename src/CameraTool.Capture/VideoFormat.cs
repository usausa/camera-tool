namespace CameraTool.Capture;

using System.Globalization;

public readonly record struct VideoFormat(VideoPixelFormat PixelFormat, int Width, int Height, double FrameRate)
{
    public override string ToString() =>
        String.Create(CultureInfo.InvariantCulture, $"{PixelFormat.ToString().ToUpperInvariant()} {Width}x{Height} {FrameRate:0.##}fps");
}
