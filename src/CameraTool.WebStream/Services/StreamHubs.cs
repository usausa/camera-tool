namespace CameraTool.WebStream.Services;

// Hubs of the stream formats
public sealed class StreamHubs
{
    // JPEG for the WebSocket (JPEG), the MJPEG and the snapshot
    public FrameHub Jpeg { get; } = new();

    // H.264 for the WebSocket (H.264)
    public FrameHub H264 { get; } = new();

    // The detections run only while a client watches either stream
    public bool HasClients => Jpeg.HasClients || H264.HasClients;
}
