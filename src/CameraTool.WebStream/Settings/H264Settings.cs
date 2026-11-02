namespace CameraTool.WebStream.Settings;

public sealed class H264Settings
{
    // WebSocket (H.264), the encoder runs only while clients are connected
    public bool Enable { get; set; } = true;

    // kbps (constant bitrate), 0: by the frame size (4000 up to 640x480, 8000 up to 1280x720, 16000 above)
    [Range(0, 100_000)]
    public int Bitrate { get; set; }

    // Seconds between key frames (a client that connects or skips a frame requests one sooner)
    [Range(1, 60)]
    public int KeyFrameInterval { get; set; } = 2;

    // False: the software encoder only
    public bool PreferHardware { get; set; } = true;
}
