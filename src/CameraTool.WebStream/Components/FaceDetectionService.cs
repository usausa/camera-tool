namespace CameraTool.WebStream.Components;

using System.Diagnostics;

using CameraTool.Capture;
using CameraTool.Vision;
using CameraTool.WebStream.Settings;

using Microsoft.ML.OnnxRuntime;

public readonly record struct FaceDetectionInfo(long Sequence, int Count);

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

    private readonly StreamHubs hubs;

    private readonly DetectionGate gate;

    private readonly AreaService area;

    private readonly AutoResetEvent signal = new(false);

    private readonly Lock resultSync = new();

    private readonly FaceBox[] workResults = new FaceBox[MaxFaces];

    private readonly FaceBox[] results = new FaceBox[MaxFaces];

    private readonly LatencyCounter preprocessTime = new();

    private readonly LatencyCounter inferenceTime = new();

    private Thread? thread;

    private volatile bool stopping;

    private volatile bool enabled;

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

    public FaceDetectionService(ILogger<FaceDetectionService> log, DetectSettings settings, VideoSource source, StreamHubs hubs, DetectionGate gate, AreaService area)
    {
        this.log = log;
        this.settings = settings;
        this.source = source;
        this.hubs = hubs;
        this.gate = gate;
        this.area = area;
        enabled = settings.Enable;
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
        // The thread does not use the execution context of the host, so it is not captured
        thread.UnsafeStart();
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

            count = area.Current.Filter(workResults.AsSpan(0, count));
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
            !hubs.HasClients ||
            (Volatile.Read(ref detector) is not { } current) ||
            gate.IsIdle(lastPreparedTimestamp) ||
            (Interlocked.CompareExchange(ref state, StatePreparing, StateIdle) != StateIdle))
        {
            return;
        }

        var frame = e.Frame;
        var start = Stopwatch.GetTimestamp();
        if (DetectionInput.TryPrepare(frame, current.Input, current.InputWidth, current.InputHeight, FaceDetector.Normalization))
        {
            lastPreparedTimestamp = start;
            preprocessTime.Add(Stopwatch.GetElapsedTime(start));
            pending = new FaceDetectionInfo(frame.Sequence, 0);
            Volatile.Write(ref state, StateReady);
            signal.Set();
        }
        else
        {
            Volatile.Write(ref state, StateIdle);
        }
    }
}
