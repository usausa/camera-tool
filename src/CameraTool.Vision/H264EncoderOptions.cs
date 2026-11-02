namespace CameraTool.Vision;

public sealed class H264EncoderOptions
{
    public int Width { get; set; }

    public int Height { get; set; }

    public int FrameRate { get; set; } = 30;

    // Bits per second (constant bitrate)
    public int Bitrate { get; set; } = 4_000_000;

    // Frames between key frames
    public int KeyFrameInterval { get; set; } = 60;

    // False: the software encoder only
    public bool PreferHardware { get; set; } = true;

    // Range of the input NV12 (also written to the stream)
    public bool FullRange { get; set; } = true;
}
