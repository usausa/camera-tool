namespace CameraTool.WebStream.Components;

// Milliseconds
public readonly record struct LatencyValue(double Average, double Max)
{
    public static LatencyValue From(LatencySnapshot snapshot) => new(snapshot.Average.TotalMilliseconds, snapshot.Max.TotalMilliseconds);
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

    public bool DetectionEnabled { get; init; }

    public string? DetectionError { get; init; }

    public double DetectFps { get; init; }

    public LatencyValue PreprocessTime { get; init; }

    public LatencyValue InferenceTime { get; init; }

    public int Faces { get; init; }

    public bool MotionEnabled { get; init; }

    // Face detection only on motion
    public bool MotionGate { get; init; }

    // Motion within the hold time
    public bool Moving { get; init; }

    // Percentage of the moving cells to the cells in the detection area in the latest frame
    public double MotionLevel { get; init; }

    public LatencyValue MotionTime { get; init; }

    public bool ObjectEnabled { get; init; }

    public string? ObjectError { get; init; }

    public double ObjectFps { get; init; }

    public LatencyValue ObjectPreprocessTime { get; init; }

    public LatencyValue ObjectInferenceTime { get; init; }

    public int Objects { get; init; }

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
