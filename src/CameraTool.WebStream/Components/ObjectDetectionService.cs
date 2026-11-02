namespace CameraTool.WebStream.Components;

using System.Diagnostics;

using CameraTool.Capture;
using CameraTool.Vision;
using CameraTool.WebStream.Settings;

using Microsoft.ML.OnnxRuntime;

public readonly record struct ObjectDetectionInfo(long Sequence, int Count);

// Detects objects (COCO classes) on a dedicated thread while clients are connected, in the same way as FaceDetectionService.
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

    private readonly VideoSource source;

    private readonly StreamHubs hubs;

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

    private ObjectDetectionInfo info;

    private long detectedFrames;

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

            log.InfoObjectDetectionChanged(value);
        }
    }

    public bool IsAvailable => Volatile.Read(ref detector) is not null;

    public string? ErrorMessage { get; private set; }

    public long DetectedFrames => Interlocked.Read(ref detectedFrames);

    public int ObjectCount
    {
        get
        {
            lock (resultSync)
            {
                return info.Count;
            }
        }
    }

    public ObjectDetectionService(ILogger<ObjectDetectionService> log, ObjectSettings settings, VideoSource source, StreamHubs hubs, DetectionGate gate, AreaService area)
    {
        this.log = log;
        this.settings = settings;
        this.source = source;
        this.hubs = hubs;
        this.gate = gate;
        this.area = area;
        interval = TimeSpan.FromMilliseconds(settings.Interval);
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
            Name = "Object detection"
        };
        thread.Start();
        if (enabled)
        {
            signal.Set();
        }

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
            Publish(enabled ? count : 0);
            Volatile.Write(ref state, StateIdle);
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
        // The results are used only by the stream
        if (!enabled ||
            !hubs.HasClients ||
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
            pending = new ObjectDetectionInfo(frame.Sequence, 0);
            Volatile.Write(ref state, StateReady);
            signal.Set();
        }
        else
        {
            Volatile.Write(ref state, StateIdle);
        }
    }
}
