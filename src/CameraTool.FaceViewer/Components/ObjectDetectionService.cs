namespace CameraTool.FaceViewer.Components;

using System.Diagnostics;

using CameraTool.Capture;
using CameraTool.FaceViewer.Settings;
using CameraTool.Vision;

using Microsoft.Extensions.Hosting;
using Microsoft.ML.OnnxRuntime;

public readonly record struct ObjectDetectionInfo(long Sequence, int FrameWidth, int FrameHeight, int Count);

// Detects objects (COCO classes) on a dedicated thread in the same way as FaceDetectionService.
// The inference takes about 100 ms, so the input is prepared at most once per interval
// (and once per idle interval while the gate waits for motion). The model is loaded when the detection is enabled first.
// Objects whose center is outside the detection area are removed from the results.
public sealed class ObjectDetectionService : IHostedService, IDisposable
{
    public const int MaxObjects = 32;

    private const int StateIdle = 0;

    private const int StatePreparing = 1;

    private const int StateReady = 2;

    private const int StateRunning = 3;

    private readonly ILogger<ObjectDetectionService> log;

    private readonly ObjectSettings settings;

    private readonly VideoSource videoSource;

    private readonly DetectionGate gate;

    private readonly AreaService area;

    private readonly TimeSpan interval;

    private readonly AutoResetEvent signal = new(false);

    private readonly Lock resultSync = new();

    private readonly ObjectBox[] workResults = new ObjectBox[MaxObjects];

    private readonly ObjectBox[] results = new ObjectBox[MaxObjects];

    private readonly LatencyCounter preprocessTime = new();

    private readonly LatencyCounter inferenceTime = new();

    private Thread? thread;

    private volatile bool stopping;

    private volatile bool enabled;

    private ObjectDetector? detector;

    private int state;

    // Stopwatch timestamp, capture thread only
    private long lastPreparedTimestamp;

    private ObjectDetectionInfo pending;

    // Generation of the results when the input was prepared
    private int pendingGeneration;

    // Incremented when the results are cleared, results of an input prepared before are not published
    private int generation;

    private ObjectDetectionInfo info;

    private long detectedFrames;

    // Raised on the detection thread
    public event EventHandler? Detected;

    public bool IsEnabled
    {
        get => enabled;
        set
        {
            enabled = value;
            if (value)
            {
                // Loads the model on the first use
                signal.Set();
            }
            else
            {
                Clear();
            }
        }
    }

    public bool IsAvailable => Volatile.Read(ref detector) is not null;

    public string? ErrorMessage { get; private set; }

    public long DetectedFrames => Interlocked.Read(ref detectedFrames);

    public ObjectDetectionService(ILogger<ObjectDetectionService> log, ObjectSettings settings, VideoSource videoSource, DetectionGate gate, AreaService area)
    {
        this.log = log;
        this.settings = settings;
        this.videoSource = videoSource;
        this.gate = gate;
        this.area = area;
        interval = TimeSpan.FromMilliseconds(settings.Interval);
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
            Name = "Object detection"
        };
        // The thread does not use the execution context of the host, so it is not captured
        thread.UnsafeStart();
        if (enabled)
        {
            signal.Set();
        }

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

    public ObjectDetectionInfo CopyResults(Span<ObjectBox> destination)
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
        ObjectDetector? current = null;
        var loaded = false;
        while (true)
        {
            signal.WaitOne();
            if (stopping)
            {
                break;
            }

            if (current is null)
            {
                // Not retried after a failure
                if (enabled && !loaded)
                {
                    loaded = true;
                    current = Load();
                }

                continue;
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

    private ObjectDetector? Load()
    {
        try
        {
            var start = Stopwatch.GetTimestamp();
            var current = new ObjectDetector(
                Path.Combine(AppContext.BaseDirectory, settings.Model),
                new ObjectDetectorOptions
                {
                    ConfidenceThreshold = settings.ConfidenceThreshold,
                    Classes = settings.GetClassIds(),
                    IntraOpNumThreads = settings.IntraOpNumThreads,
                    InterOpNumThreads = settings.InterOpNumThreads
                });
            // Owned by the service from here (disposed in Dispose)
            detector = current;
            log.InfoObjectDetectorLoaded(settings.Model, current.InputWidth, current.InputHeight, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            return current;
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or IOException or InvalidOperationException or DllNotFoundException or TypeInitializationException)
        {
            log.ErrorObjectDetectorLoadFailed(ex, settings.Model);
            ErrorMessage = ex.Message;
            return null;
        }
    }

    //--------------------------------------------------------------------------------
    // Capture thread
    //--------------------------------------------------------------------------------

    private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
    {
        if (!enabled ||
            (Volatile.Read(ref detector) is not { } current) ||
            (Stopwatch.GetElapsedTime(lastPreparedTimestamp) < interval) ||
            gate.IsIdle(lastPreparedTimestamp) ||
            (Interlocked.CompareExchange(ref state, StatePreparing, StateIdle) != StateIdle))
        {
            return;
        }

        var frame = e.Frame;
        var start = Stopwatch.GetTimestamp();
        if (DetectionInput.TryPrepare(frame, current.Input, current.InputWidth, current.InputHeight, ObjectDetector.Normalization))
        {
            lastPreparedTimestamp = start;
            preprocessTime.Add(Stopwatch.GetElapsedTime(start));
            pending = new ObjectDetectionInfo(frame.Sequence, frame.Width, frame.Height, 0);
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
