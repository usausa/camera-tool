namespace CameraTool.WebStream.Settings;

public sealed class H264SettingsTests
{
    [Theory]
    [InlineData(320, 240, 4000)]
    [InlineData(640, 480, 4000)]
    [InlineData(800, 600, 8000)]
    [InlineData(1280, 720, 8000)]
    [InlineData(1920, 1080, 16000)]
    public void BitrateFollowsFrameSize(int width, int height, int expected) =>
        Assert.Equal(expected, new H264Settings().ResolveBitrate(width, height));

    [Fact]
    public void ConfiguredBitrateIsUsed() =>
        Assert.Equal(2500, new H264Settings { Bitrate = 2500 }.ResolveBitrate(1920, 1080));
}
