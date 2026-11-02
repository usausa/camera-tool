namespace CameraTool.FaceViewer.Components;

using System.Diagnostics;

using CameraTool.Capture;
using CameraTool.FaceViewer.Settings;
using CameraTool.Vision;

using Microsoft.Extensions.Hosting;
using Microsoft.ML.OnnxRuntime;

public readonly record struct FaceDetectionInfo(long Sequence, int FrameWidth, int FrameHeight, int Count);

// Detects faces on a dedicated thread.
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

    private readonly VideoSource videoSource;

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

    // Generation of the results when the input was prepared
    private int pendingGeneration;

    // Incremented when the results are cleared, results of an input prepared before are not published
    private int generation;

    private FaceDetectionInfo info;

    private long detectedFrames;

    // Raised on the detection thread
    public event EventHandler? Detected;

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

    // DetectionGate depends on MotionService, so MotionService subscribes to the frames first
    public FaceDetectionService(ILogger<FaceDetectionService> log, DetectSettings settings, VideoSource videoSource, DetectionGate gate, AreaService area)
    {
        this.log = log;
        this.settings = settings;
        this.videoSource = videoSource;
        this.gate = gate;
        this.area = area;
        enabled = settings.Enable;

        videoSource.FrameCaptured += OnFrameCaptured;
        videoSource.StateChanged += OnStateChanged;
    }

    public void Dispose()
    {
        videoSource.FrameCaptured -= OnFrameCaptured;
        videoSource.StateChanged -= OnStateChanged;
        detector?.Dispose();
        signal.Dispose();
    }

    //--------------------------------------------------------------------------------
    // Hosted service
    //--------------------------------------------------------------------------------

    public Task StartAsync(CancellationToken cancellationToken)
    {
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
        videoSource.FrameCaptured -= OnFrameCaptured;
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

    // False when the results were cleared after the input was prepared (the camera stopped during the inference)
    private bool Publish(int count)
    {
        lock (resultSync)
        {
            if (pendingGeneration != generation)
            {
                return false;
            }

            workResults.AsSpan(0, count).CopyTo(results);
            info = pending with { Count = count };
            return true;
        }
    }

    private void Clear()
    {
        lock (resultSync)
        {
            generation++;
            info = default;
        }

        Detected?.Invoke(this, EventArgs.Empty);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (!videoSource.IsRunning)
        {
            Clear();
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
            var published = Publish(enabled ? count : 0);
            Volatile.Write(ref state, StateIdle);
            if (published)
            {
                Detected?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    //--------------------------------------------------------------------------------
    // Capture thread
    //--------------------------------------------------------------------------------

    private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
    {
        if (!enabled ||
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
            pending = new FaceDetectionInfo(frame.Sequence, frame.Width, frame.Height, 0);
            pendingGeneration = Volatile.Read(ref generation);
            Volatile.Write(ref state, StateReady);
            signal.Set();
        }
        else
        {
            Volatile.Write(ref state, StateIdle);
        }
    }
}
