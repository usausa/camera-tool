namespace CameraTool.Vision;

using System.Diagnostics;

// Frame the results were detected in (Count: number of the results)
public readonly record struct DetectionInfo(long Sequence, int FrameWidth, int FrameHeight, int Count);

// Runs a detector on a dedicated thread for the frames of the capture thread, shared by the face and object detection.
// The capture thread prepares the input only when the detector is idle (TryBeginPrepare and EndPrepare), so detection
// never blocks capture, and the input is prepared at most once per interval.
// The detection thread runs the inference, removes the boxes whose center is outside the detection area and publishes the results.
// The detector is loaded on the detection thread when the detection is enabled first (not retried after a failure).
public sealed class DetectionWorker<TBox> : IDisposable
    where TBox : struct, IDetectionBox
{
    private const int StateIdle = 0;

    private const int StatePreparing = 1;

    private const int StateReady = 2;

    private const int StateRunning = 3;

    private readonly string name;

    private readonly TimeSpan interval;

    private readonly Func<IDetector<TBox>?> load;

    private readonly Func<DetectionArea> area;

    private readonly AutoResetEvent signal = new(false);

    private readonly Lock resultSync = new();

    private readonly TBox[] workResults;

    private readonly TBox[] results;

    private readonly LatencyCounter preprocessTime = new();

    private readonly LatencyCounter inferenceTime = new();

    private Thread? thread;

    private volatile bool stopping;

    private volatile bool enabled;

    private IDetector<TBox>? detector;

    private int state;

    // Stopwatch timestamp, capture thread only
    private long prepareTimestamp;

    private DetectionInfo pending;

    // Generation of the results when the input was prepared
    private int pendingGeneration;

    // Incremented when the results are cleared, results of an input prepared before are not published
    private int generation;

    private DetectionInfo info;

    private long detectedFrames;

    // Raised on the detection thread when results are published, and on the thread that clears them
    public event EventHandler? Detected;

    public bool IsEnabled
    {
        get => enabled;
        set
        {
            enabled = value;
            if (value)
            {
                // Loads the detector on the first use
                signal.Set();
            }
            else
            {
                Clear();
            }
        }
    }

    public bool IsAvailable => Volatile.Read(ref detector) is not null;

    public long DetectedFrames => Interlocked.Read(ref detectedFrames);

    public int Count
    {
        get
        {
            lock (resultSync)
            {
                return info.Count;
            }
        }
    }

    // Stopwatch timestamp of the last prepared input, capture thread only
    public long LastPreparedTimestamp { get; private set; }

    // name: of the thread, capacity: maximum number of the results, interval: minimum time between the inputs,
    // load: creates the detector on the detection thread (null when it fails), area: the current detection area
    public DetectionWorker(string name, int capacity, TimeSpan interval, Func<IDetector<TBox>?> load, Func<DetectionArea> area)
    {
        this.name = name;
        this.interval = interval;
        this.load = load;
        this.area = area;
        workResults = new TBox[capacity];
        results = new TBox[capacity];
    }

    // Stops the thread if it is still running, can be called more than once
    public void Dispose()
    {
        Stop();
        Interlocked.Exchange(ref detector, null)?.Dispose();
        signal.Dispose();
    }

    public void Start()
    {
        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = name
        };
        // The thread does not use the execution context of the caller, so it is not captured
        thread.UnsafeStart();
    }

    public void Stop()
    {
        if (!stopping)
        {
            stopping = true;
            signal.Set();
        }

        thread?.Join();
    }

    //--------------------------------------------------------------------------------
    // Result
    //--------------------------------------------------------------------------------

    public DetectionInfo CopyResults(Span<TBox> destination)
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

    // Results of an input prepared before are not published (the detection was disabled or the camera stopped during the inference)
    public void Clear()
    {
        lock (resultSync)
        {
            generation++;
            info = default;
        }

        Detected?.Invoke(this, EventArgs.Empty);
    }

    // False when the results were cleared after the input was prepared
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

    //--------------------------------------------------------------------------------
    // Detection thread
    //--------------------------------------------------------------------------------

    private void Run()
    {
        IDetector<TBox>? current = null;
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
                    current = load();
                    // Owned by the worker from here (disposed in Dispose)
                    Volatile.Write(ref detector, current);
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

            count = area().Filter(workResults.AsSpan(0, count));
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

    // The detector to prepare the input of, null while the detection is disabled, not loaded, busy or within the interval
    public IDetector<TBox>? TryBeginPrepare()
    {
        if (!enabled ||
            (Volatile.Read(ref detector) is not { } current) ||
            (Stopwatch.GetElapsedTime(LastPreparedTimestamp) < interval) ||
            (Interlocked.CompareExchange(ref state, StatePreparing, StateIdle) != StateIdle))
        {
            return null;
        }

        prepareTimestamp = Stopwatch.GetTimestamp();
        return current;
    }

    // After TryBeginPrepare, prepared: false when the input could not be written (the detector stays idle)
    public void EndPrepare(bool prepared, long sequence, int frameWidth, int frameHeight)
    {
        if (!prepared)
        {
            Volatile.Write(ref state, StateIdle);
            return;
        }

        LastPreparedTimestamp = prepareTimestamp;
        preprocessTime.Add(Stopwatch.GetElapsedTime(prepareTimestamp));
        pending = new DetectionInfo(sequence, frameWidth, frameHeight, 0);
        pendingGeneration = Volatile.Read(ref generation);
        Volatile.Write(ref state, StateReady);
        signal.Set();
    }
}
