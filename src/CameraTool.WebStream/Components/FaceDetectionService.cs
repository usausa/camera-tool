namespace CameraTool.WebStream.Components;

using System.Diagnostics;

using CameraTool.Capture;
using CameraTool.Vision;
using CameraTool.WebStream.Settings;

using Microsoft.ML.OnnxRuntime;

public readonly record struct FaceDetectionInfo(long Sequence, int FrameWidth, int FrameHeight, int Count);

// Detects faces on a dedicated thread while clients are connected.
// The capture thread prepares the input only when the detector is idle, so detection never blocks capture.
// With the motion gate, the input is prepared only once per idle interval while there is no motion.
// Faces whose center is outside the detection area are removed from the results.
public sealed class FaceDetectionService : IHostedService, IDisposable
{
    public const int MaxFaces = 32;

    private const int StateIdle = 0;

    private const int StatePreparing = 1;

    private const int StateReady = 2;

    private const int StateRunning = 3;

    private readonly ILogger<FaceDetectionService> log;

    private readonly DetectSettings settings;

    private readonly VideoSource source;

    private readonly FrameHub hub;

    private readonly MotionService motion;

    private readonly AreaService area;

    private readonly TimeSpan idleInterval;

    private readonly TimeSpan motionHoldTime;

    private readonly AutoResetEvent signal = new(false);

    private readonly Lock resultSync = new();

    private readonly FaceBox[] workResults = new FaceBox[MaxFaces];

    private readonly FaceBox[] results = new FaceBox[MaxFaces];

    private readonly LatencyCounter preprocessTime = new();

    private readonly LatencyCounter inferenceTime = new();

    private Thread? thread;

    private volatile bool stopping;

    private volatile bool enabled;

    private volatile bool motionGate;

    private FaceDetector? detector;

    private int state;

    // Stopwatch timestamp, capture thread only
    private long lastPreparedTimestamp;

    private FaceDetectionInfo pending;

    private FaceDetectionInfo info;

    private long detectedFrames;

    public bool IsEnabled
    {
        get => enabled;
        set
        {
            enabled = value;
            if (!value)
            {
                Clear();
            }

            log.InfoFaceDetectionChanged(value);
        }
    }

    public bool IsMotionGateEnabled
    {
        get => motionGate;
        set
        {
            motionGate = value;
            log.InfoMotionGateChanged(value);
        }
    }

    public bool IsAvailable => Volatile.Read(ref detector) is not null;

    public string? ErrorMessage { get; private set; }

    public long DetectedFrames => Interlocked.Read(ref detectedFrames);

    public int FaceCount
    {
        get
        {
            lock (resultSync)
            {
                return info.Count;
            }
        }
    }

    public FaceDetectionService(ILogger<FaceDetectionService> log, DetectSettings settings, VideoSource source, FrameHub hub, MotionService motion, AreaService area)
    {
        this.log = log;
        this.settings = settings;
        this.source = source;
        this.hub = hub;
        this.motion = motion;
        this.area = area;
        idleInterval = TimeSpan.FromMilliseconds(settings.IdleInterval);
        motionHoldTime = TimeSpan.FromMilliseconds(settings.MotionHoldTime);
        enabled = settings.Enable;
        motionGate = settings.MotionGate;
    }

    public void Dispose()
    {
        detector?.Dispose();
        signal.Dispose();
    }

    //--------------------------------------------------------------------------------
    // Hosted service
    //--------------------------------------------------------------------------------

    public Task StartAsync(CancellationToken cancellationToken)
    {
        source.FrameCaptured += OnFrameCaptured;
        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Face detection"
        };
        thread.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        source.FrameCaptured -= OnFrameCaptured;
        stopping = true;
        signal.Set();
        thread?.Join();
        return Task.CompletedTask;
    }

    //--------------------------------------------------------------------------------
    // Result
    //--------------------------------------------------------------------------------

    public FaceDetectionInfo CopyResults(Span<FaceBox> destination)
    {
        lock (resultSync)
        {
            var count = Math.Min(info.Count, destination.Length);
            results.AsSpan(0, count).CopyTo(destination);
            return info with { Count = count };
        }
    }

    public LatencySnapshot TakePreprocessTime() => preprocessTime.Take();

    public LatencySnapshot TakeInferenceTime() => inferenceTime.Take();

    private void Publish(int count)
    {
        lock (resultSync)
        {
            workResults.AsSpan(0, count).CopyTo(results);
            info = pending with { Count = count };
        }
    }

    private void Clear()
    {
        lock (resultSync)
        {
            info = default;
        }
    }

    //--------------------------------------------------------------------------------
    // Detection thread
    //--------------------------------------------------------------------------------

    private void Run()
    {
        FaceDetector current;
        try
        {
            var start = Stopwatch.GetTimestamp();
            current = new FaceDetector(
                Path.Combine(AppContext.BaseDirectory, settings.Model),
                new FaceDetectorOptions
                {
                    ConfidenceThreshold = settings.ConfidenceThreshold,
                    IouThreshold = settings.IouThreshold,
                    IntraOpNumThreads = settings.IntraOpNumThreads,
                    InterOpNumThreads = settings.InterOpNumThreads
                });
            // Owned by the service from here (disposed in Dispose)
            detector = current;
            log.InfoFaceDetectorLoaded(settings.Model, current.InputWidth, current.InputHeight, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or IOException or InvalidOperationException or DllNotFoundException or TypeInitializationException)
        {
            log.ErrorFaceDetectorLoadFailed(ex, settings.Model);
            ErrorMessage = ex.Message;
            return;
        }

        while (true)
        {
            signal.WaitOne();
            if (stopping)
            {
                break;
            }

            if (Interlocked.CompareExchange(ref state, StateRunning, StateReady) != StateReady)
            {
                continue;
            }

            var start = Stopwatch.GetTimestamp();
            var count = current.Detect(workResults);
            inferenceTime.Add(Stopwatch.GetElapsedTime(start));
            Interlocked.Increment(ref detectedFrames);

            count = area.Current.FilterFaces(workResults.AsSpan(0, count));
            Publish(enabled ? count : 0);
            Volatile.Write(ref state, StateIdle);
        }
    }

    //--------------------------------------------------------------------------------
    // Capture thread
    //--------------------------------------------------------------------------------

    private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
    {
        // The results are used only by the stream
        if (!enabled ||
            !hub.HasClients ||
            (Volatile.Read(ref detector) is not { } current) ||
            IsIdle() ||
            (Interlocked.CompareExchange(ref state, StatePreparing, StateIdle) != StateIdle))
        {
            return;
        }

        var frame = e.Frame;
        var start = Stopwatch.GetTimestamp();
        if (Prepare(current, frame))
        {
            lastPreparedTimestamp = start;
            preprocessTime.Add(Stopwatch.GetElapsedTime(start));
            pending = new FaceDetectionInfo(frame.Sequence, frame.Width, frame.Height, 0);
            Volatile.Write(ref state, StateReady);
            signal.Set();
        }
        else
        {
            Volatile.Write(ref state, StateIdle);
        }
    }

    private bool IsIdle() =>
        motionGate &&
        motion.IsEnabled &&
        !motion.IsMoving(motionHoldTime) &&
        (Stopwatch.GetElapsedTime(lastPreparedTimestamp) < idleInterval);

    private static bool Prepare(FaceDetector detector, in FrameBuffer frame)
    {
        var input = detector.Input;
        var range = frame.ColorRange == VideoColorRange.Full ? YuvRange.Full : YuvRange.Limited;
        try
        {
            switch (frame.PixelFormat)
            {
                case VideoPixelFormat.Bgra8:
                    TensorConverter.BgraToTensor(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, frame.Width, frame.Height, input, detector.InputWidth, detector.InputHeight);
                    return true;
                case VideoPixelFormat.Nv12:
                    TensorConverter.Nv12ToTensor(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, frame.GetPlaneSpan(frame.Plane1), frame.Plane1.Stride, frame.Width, frame.Height, range, input, detector.InputWidth, detector.InputHeight);
                    return true;
                case VideoPixelFormat.Yuy2:
                    TensorConverter.Yuy2ToTensor(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, frame.Width, frame.Height, range, input, detector.InputWidth, detector.InputHeight);
                    return true;
                default:
                    return false;
            }
        }
        catch (ArgumentException)
        {
            // Unexpected buffer layout
            return false;
        }
    }
}
