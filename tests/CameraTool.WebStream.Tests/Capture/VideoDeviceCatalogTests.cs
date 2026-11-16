namespace CameraTool.WebStream.Capture;

using CameraTool.Capture;

using Windows.Media.Capture.Frames;

public sealed class VideoDeviceCatalogTests
{
    [Theory]
    [InlineData(true, MediaFrameSourceKind.Color)]
    [InlineData(true, MediaFrameSourceKind.Color, MediaFrameSourceKind.Color)]
    [InlineData(true, MediaFrameSourceKind.Color, MediaFrameSourceKind.Custom)]
    [InlineData(false, MediaFrameSourceKind.Custom)]
    [InlineData(false, MediaFrameSourceKind.Color, MediaFrameSourceKind.Infrared)]
    [InlineData(false, MediaFrameSourceKind.Infrared)]
    [InlineData(false, MediaFrameSourceKind.Depth, MediaFrameSourceKind.Infrared)]
    [InlineData(false)]
    public void ColorCameraIsFoundBySourceKinds(bool expected, params MediaFrameSourceKind[] kinds)
    {
        var result = VideoDeviceCatalog.IsColorCamera(kinds);

        Assert.Equal(expected, result);
    }
}
