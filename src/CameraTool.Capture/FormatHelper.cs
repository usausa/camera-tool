namespace CameraTool.Capture;

using Windows.Graphics.Imaging;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

internal static class FormatHelper
{
    private const double FrameRateTolerance = 0.5;

    public static VideoPixelFormat FromSubtype(string subtype) => subtype.ToUpperInvariant() switch
    {
        "NV12" => VideoPixelFormat.Nv12,
        "YUY2" => VideoPixelFormat.Yuy2,
        "BGRA8" => VideoPixelFormat.Bgra8,
        _ => VideoPixelFormat.Unknown
    };

    public static string ToSubtype(VideoPixelFormat format) => format switch
    {
        VideoPixelFormat.Nv12 => "NV12",
        VideoPixelFormat.Yuy2 => "YUY2",
        VideoPixelFormat.Bgra8 => "BGRA8",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static VideoPixelFormat FromBitmapPixelFormat(BitmapPixelFormat format) => format switch
    {
        BitmapPixelFormat.Nv12 => VideoPixelFormat.Nv12,
        BitmapPixelFormat.Yuy2 => VideoPixelFormat.Yuy2,
        BitmapPixelFormat.Bgra8 => VideoPixelFormat.Bgra8,
        _ => VideoPixelFormat.Unknown
    };

    public static double ToFrameRate(MediaRatio ratio) =>
        ratio.Denominator == 0 ? 0 : (double)ratio.Numerator / ratio.Denominator;

    public static VideoFormat ToVideoFormat(MediaFrameFormat format) =>
        new(FromSubtype(format.Subtype), (int)format.VideoFormat.Width, (int)format.VideoFormat.Height, ToFrameRate(format.FrameRate));

    public static bool IsMatch(MediaFrameFormat format, VideoFormat target)
    {
        var source = ToVideoFormat(format);
        return (source.PixelFormat == target.PixelFormat) &&
               (source.Width == target.Width) &&
               (source.Height == target.Height) &&
               (Math.Abs(source.FrameRate - target.FrameRate) < FrameRateTolerance);
    }
}
