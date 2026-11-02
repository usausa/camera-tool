namespace CameraTool.WebStream.Components;

using System.Diagnostics;

// Statistics updated every second
public sealed class StatusService : BackgroundService
{
    private const int LogInterval = 5;

    private const double BytesPerKilobyte = 1024;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly ILogger<StatusService> log;

    private readonly TimeProvider timeProvider;

    private readonly VideoSource source;

    private readonly StreamService stream;

    private readonly FrameHub hub;

    private readonly FaceDetectionService detection;

    private StreamStatus current = new();

    public StreamStatus Current => Volatile.Read(ref current);

    public StatusService(ILogger<StatusService> log, TimeProvider timeProvider, VideoSource source, StreamService stream, FrameHub hub, FaceDetectionService detection)
    {
        this.log = log;
        this.timeProvider = timeProvider;
        this.source = source;
        this.stream = stream;
        this.hub = hub;
        this.detection = detection;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        var last = TakeCounters();
        var count = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var counters = TakeCounters();
                var elapsed = timeProvider.GetElapsedTime(last.Timestamp, counters.Timestamp).TotalSeconds;
                if (elapsed <= 0)
                {
                    continue;
                }

                var status = CreateStatus(last, counters, elapsed);
                Volatile.Write(ref current, status);
                last = counters;

                count++;
                if (status.Running && (count % LogInterval == 0))
                {
                    WriteLog(status);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped
        }
    }

    private Counters TakeCounters()
    {
        using var process = Process.GetCurrentProcess();
        return new Counters(
            timeProvider.GetTimestamp(),
            source.CapturedFrames,
            stream.EncodedFrames,
            stream.EncodedBytes,
            hub.SentFrames,
            hub.SkippedFrames,
            detection.DetectedFrames,
            process.TotalProcessorTime,
            GC.GetTotalAllocatedBytes(),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));
    }

    private StreamStatus CreateStatus(Counters last, Counters counters, double elapsed)
    {
        var encoded = counters.Encoded - last.Encoded;
        return new StreamStatus
        {
            Device = source.Device?.Name,
            Format = source.Format?.ToString(),
            Running = source.IsRunning,
            Error = source.ErrorMessage,
            Clients = hub.ClientCount,
            CaptureFps = (counters.Captured - last.Captured) / elapsed,
            EncodeFps = encoded / elapsed,
            SendFps = (counters.Sent - last.Sent) / elapsed,
            SkipFps = (counters.Skipped - last.Skipped) / elapsed,
            CaptureLatency = LatencyValue.From(source.TakeCaptureLatency()),
            ConvertTime = LatencyValue.From(stream.TakeConvertTime()),
            EncodeTime = LatencyValue.From(stream.TakeEncodeTime()),
            StreamLatency = LatencyValue.From(stream.TakeStreamLatency()),
            SendStartLatency = LatencyValue.From(hub.TakeSendStartLatency()),
            SentLatency = LatencyValue.From(hub.TakeSentLatency()),
            JpegSize = encoded > 0 ? (counters.EncodedBytes - last.EncodedBytes) / BytesPerKilobyte / encoded : 0,
            Buffers = stream.AllocatedFrames,
            DetectionEnabled = detection.IsEnabled,
            DetectionError = detection.ErrorMessage,
            DetectFps = (counters.Detected - last.Detected) / elapsed,
            PreprocessTime = LatencyValue.From(detection.TakePreprocessTime()),
            InferenceTime = LatencyValue.From(detection.TakeInferenceTime()),
            Faces = detection.FaceCount,
            CpuUsage = (counters.ProcessorTime - last.ProcessorTime).TotalSeconds / elapsed / Environment.ProcessorCount * 100,
            Allocated = (counters.Allocated - last.Allocated) / BytesPerKilobyte / elapsed,
            Gc0 = (counters.Gc0 - last.Gc0) / elapsed,
            Gc1 = (counters.Gc1 - last.Gc1) / elapsed,
            Gc2 = (counters.Gc2 - last.Gc2) / elapsed
        };
    }

    private void WriteLog(StreamStatus status)
    {
        log.DebugStatistics(
            status.Format ?? string.Empty,
            status.Clients,
            status.CaptureFps,
            status.EncodeFps,
            status.SendFps,
            status.SkipFps,
            status.CaptureLatency.Average,
            status.CaptureLatency.Max,
            status.ConvertTime.Average,
            status.ConvertTime.Max,
            status.EncodeTime.Average,
            status.EncodeTime.Max,
            status.StreamLatency.Average,
            status.StreamLatency.Max,
            status.SendStartLatency.Average,
            status.SendStartLatency.Max,
            status.SentLatency.Average,
            status.SentLatency.Max,
            status.JpegSize,
            status.CpuUsage,
            status.Allocated,
            status.Gc0,
            status.Gc1,
            status.Gc2);
        log.DebugDetectionStatistics(
            status.DetectionEnabled,
            status.DetectFps,
            status.PreprocessTime.Average,
            status.PreprocessTime.Max,
            status.InferenceTime.Average,
            status.InferenceTime.Max,
            status.Faces);
    }

    private readonly record struct Counters(
        long Timestamp,
        long Captured,
        long Encoded,
        long EncodedBytes,
        long Sent,
        long Skipped,
        long Detected,
        TimeSpan ProcessorTime,
        long Allocated,
        int Gc0,
        int Gc1,
        int Gc2);
}
