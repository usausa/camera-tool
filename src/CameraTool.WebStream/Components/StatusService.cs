namespace CameraTool.WebStream.Components;

using System.Diagnostics;

using CameraTool.WebStream.Settings;

// Statistics updated every second
public sealed class StatusService : BackgroundService
{
    private const int LogInterval = 5;

    private const double BytesPerKilobyte = 1024;

    private const double BitsPerKilobit = 1000;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly ILogger<StatusService> log;

    private readonly TimeProvider timeProvider;

    private readonly VideoSource source;

    private readonly StreamService stream;

    private readonly FrameHub hub;

    private readonly H264StreamService h264;

    private readonly FrameHub h264Hub;

    private readonly FaceDetectionService detection;

    private readonly MotionService motion;

    private readonly DetectionGate gate;

    private readonly ObjectDetectionService objectDetection;

    private readonly AreaService area;

    private readonly TimeSpan motionHoldTime;

    private StreamStatus current = new();

    public StreamStatus Current => Volatile.Read(ref current);

    public StatusService(
        ILogger<StatusService> log,
        TimeProvider timeProvider,
        DetectSettings detectSettings,
        VideoSource source,
        StreamService stream,
        H264StreamService h264,
        StreamHubs hubs,
        FaceDetectionService detection,
        MotionService motion,
        DetectionGate gate,
        ObjectDetectionService objectDetection,
        AreaService area)
    {
        this.log = log;
        this.timeProvider = timeProvider;
        this.source = source;
        this.stream = stream;
        this.h264 = h264;
        hub = hubs.Jpeg;
        h264Hub = hubs.H264;
        this.detection = detection;
        this.motion = motion;
        this.gate = gate;
        this.objectDetection = objectDetection;
        this.area = area;
        motionHoldTime = TimeSpan.FromMilliseconds(detectSettings.MotionHoldTime);
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
            h264.EncodedFrames,
            h264.EncodedBytes,
            h264.KeyFrames,
            h264.DroppedFrames,
            h264Hub.SentFrames,
            h264Hub.SkippedFrames,
            detection.DetectedFrames,
            objectDetection.DetectedFrames,
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
            H264Enabled = h264.IsEnabled,
            H264Encoder = h264.EncoderName,
            H264Hardware = h264.IsHardware,
            H264Error = h264.ErrorMessage,
            H264Clients = h264Hub.ClientCount,
            H264EncodeFps = (counters.H264Encoded - last.H264Encoded) / elapsed,
            H264SendFps = (counters.H264Sent - last.H264Sent) / elapsed,
            H264SkipFps = (counters.H264Skipped - last.H264Skipped) / elapsed,
            H264DropFps = (counters.H264Dropped - last.H264Dropped) / elapsed,
            H264KeyFrameFps = (counters.H264KeyFrames - last.H264KeyFrames) / elapsed,
            H264InputTime = LatencyValue.From(h264.TakeInputTime()),
            H264EncodeTime = LatencyValue.From(h264.TakeEncodeTime()),
            H264StreamLatency = LatencyValue.From(h264.TakeStreamLatency()),
            H264SentLatency = LatencyValue.From(h264Hub.TakeSentLatency()),
            H264Bitrate = (counters.H264EncodedBytes - last.H264EncodedBytes) * 8 / BitsPerKilobit / elapsed,
            H264Buffers = h264.AllocatedFrames,
            DetectionEnabled = detection.IsEnabled,
            DetectionError = detection.ErrorMessage,
            DetectFps = (counters.Detected - last.Detected) / elapsed,
            PreprocessTime = LatencyValue.From(detection.TakePreprocessTime()),
            InferenceTime = LatencyValue.From(detection.TakeInferenceTime()),
            Faces = detection.FaceCount,
            MotionEnabled = motion.IsEnabled,
            MotionGate = gate.IsEnabled,
            Moving = motion.IsEnabled && motion.IsMoving(motionHoldTime),
            MotionLevel = motion.Info.Level * 100,
            MotionTime = LatencyValue.From(motion.TakeProcessTime()),
            ObjectEnabled = objectDetection.IsEnabled,
            ObjectError = objectDetection.ErrorMessage,
            ObjectFps = (counters.ObjectDetected - last.ObjectDetected) / elapsed,
            ObjectPreprocessTime = LatencyValue.From(objectDetection.TakePreprocessTime()),
            ObjectInferenceTime = LatencyValue.From(objectDetection.TakeInferenceTime()),
            Objects = objectDetection.ObjectCount,
            AreaVersion = area.Version,
            AreaCells = area.Current.CellCount,
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
        log.DebugH264Statistics(
            status.H264Clients,
            status.H264EncodeFps,
            status.H264SendFps,
            status.H264SkipFps,
            status.H264DropFps,
            status.H264KeyFrameFps,
            status.H264InputTime.Average,
            status.H264InputTime.Max,
            status.H264EncodeTime.Average,
            status.H264EncodeTime.Max,
            status.H264StreamLatency.Average,
            status.H264StreamLatency.Max,
            status.H264SentLatency.Average,
            status.H264SentLatency.Max,
            status.H264Bitrate);
        log.DebugDetectionStatistics(
            status.DetectionEnabled,
            status.DetectFps,
            status.PreprocessTime.Average,
            status.PreprocessTime.Max,
            status.InferenceTime.Average,
            status.InferenceTime.Max,
            status.Faces);
        log.DebugMotionStatistics(
            status.MotionEnabled,
            status.MotionGate,
            status.Moving,
            status.MotionLevel,
            status.MotionTime.Average,
            status.MotionTime.Max);
        log.DebugObjectStatistics(
            status.ObjectEnabled,
            status.ObjectFps,
            status.ObjectPreprocessTime.Average,
            status.ObjectPreprocessTime.Max,
            status.ObjectInferenceTime.Average,
            status.ObjectInferenceTime.Max,
            status.Objects);
    }

    private readonly record struct Counters(
        long Timestamp,
        long Captured,
        long Encoded,
        long EncodedBytes,
        long Sent,
        long Skipped,
        long H264Encoded,
        long H264EncodedBytes,
        long H264KeyFrames,
        long H264Dropped,
        long H264Sent,
        long H264Skipped,
        long Detected,
        long ObjectDetected,
        TimeSpan ProcessorTime,
        long Allocated,
        int Gc0,
        int Gc1,
        int Gc2);
}
