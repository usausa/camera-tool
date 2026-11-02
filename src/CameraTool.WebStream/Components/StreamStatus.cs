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

    public bool DetectionEnabled { get; init; }

    public string? DetectionError { get; init; }

    public double DetectFps { get; init; }

    public LatencyValue PreprocessTime { get; init; }

    public LatencyValue InferenceTime { get; init; }

    public int Faces { get; init; }

    public double CpuUsage { get; init; }

    // KB/s
    public double Allocated { get; init; }

    public double Gc0 { get; init; }

    public double Gc1 { get; init; }

    public double Gc2 { get; init; }
}
