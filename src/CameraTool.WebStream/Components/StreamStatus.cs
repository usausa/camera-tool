namespace CameraTool.WebStream.Components;

using CameraTool.Vision;

// Statistics of the face or object detection (the same items for both)
public sealed class DetectorStatus
{
    public bool Enabled { get; init; }

    // Set when the model cannot be loaded
    public string? Error { get; init; }

    public double Fps { get; init; }

    public LatencyValue PreprocessTime { get; init; }

    public LatencyValue InferenceTime { get; init; }

    // Results of the latest detection
    public int Count { get; init; }
}

public sealed class StreamStatus
{
    public string? Device { get; init; }

    public string? Format { get; init; }

    public bool Running { get; init; }

    public string? Error { get; init; }

    public int Clients { get; init; }

    public double CaptureFps { get; init; }

    public double EncodeFps { get; init; }

    public double SendFps { get; init; }

    public double SkipFps { get; init; }

    public LatencyValue CaptureLatency { get; init; }

    public LatencyValue ConvertTime { get; init; }

    public LatencyValue EncodeTime { get; init; }

    // Arrival -> published
    public LatencyValue StreamLatency { get; init; }

    // Published -> send start
    public LatencyValue SendStartLatency { get; init; }

    // Published -> sent
    public LatencyValue SentLatency { get; init; }

    // KB
    public double JpegSize { get; init; }

    // Encoded frames allocated by the pool
    public int Buffers { get; init; }

    public bool H264Enabled { get; init; }

    // Encoder used last
    public string? H264Encoder { get; init; }

    public bool H264Hardware { get; init; }

    public string? H264Error { get; init; }

    public int H264Clients { get; init; }

    public double H264EncodeFps { get; init; }

    public double H264SendFps { get; init; }

    public double H264SkipFps { get; init; }

    // Frames not encoded because the encoder was busy
    public double H264DropFps { get; init; }

    public double H264KeyFrameFps { get; init; }

    // Frame -> encoder input (conversion and copy)
    public LatencyValue H264InputTime { get; init; }

    // Encoder input -> output
    public LatencyValue H264EncodeTime { get; init; }

    // Arrival -> published
    public LatencyValue H264StreamLatency { get; init; }

    // Published -> send start
    public LatencyValue H264SendStartLatency { get; init; }

    // Published -> sent
    public LatencyValue H264SentLatency { get; init; }

    // kbps
    public double H264Bitrate { get; init; }

    public int H264Buffers { get; init; }

    public DetectorStatus Faces { get; init; } = new();

    public bool MotionEnabled { get; init; }

    // Face detection only on motion
    public bool MotionGate { get; init; }

    // Motion within the hold time
    public bool Moving { get; init; }

    // Percentage of the moving cells to the cells in the detection area in the latest frame
    public double MotionLevel { get; init; }

    public LatencyValue MotionTime { get; init; }

    public DetectorStatus Objects { get; init; } = new();

    // Incremented when the detection area changes
    public int AreaVersion { get; init; }

    // Cells in the detection area (all cells when it is the full area)
    public int AreaCells { get; init; }

    public double CpuUsage { get; init; }

    // KB/s
    public double Allocated { get; init; }

    public double Gc0 { get; init; }

    public double Gc1 { get; init; }

    public double Gc2 { get; init; }
}
