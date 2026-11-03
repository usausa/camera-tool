namespace CameraTool.FaceViewer.Components;

using System.Diagnostics;

using CameraTool.Capture;
using CameraTool.FaceViewer.Settings;
using CameraTool.Vision;

using Microsoft.Extensions.Hosting;
using Microsoft.ML.OnnxRuntime;

// Detects objects (COCO classes) in the same way as FaceDetectionService.
// The inference takes about 100 ms, so the input is prepared at most once per interval
// (and once per idle interval while the gate waits for motion). The model is loaded when the detection is enabled first.
// Objects whose center is outside the detection area are removed from the results.
public sealed class ObjectDetectionService : IHostedService, IDisposable, IDetectionStatistics
{
    public const int MaxObjects = 32;

    private readonly ILogger<ObjectDetectionService> log;

    private readonly ObjectSettings settings;

    private readonly VideoSource videoSource;

    private readonly DetectionGate gate;

    private readonly DetectionWorker<ObjectBox> worker;

    // Raised on the detection thread, and on the thread that clears the results
    public event EventHandler? Detected
    {
        add => worker.Detected += value;
        remove => worker.Detected -= value;
    }

    public bool IsEnabled
    {
        get => worker.IsEnabled;
        set => worker.IsEnabled = value;
    }

    public bool IsAvailable => worker.IsAvailable;

    // Set on the detection thread when the model cannot be loaded
    public string? ErrorMessage { get; private set; }

    public long DetectedFrames => worker.DetectedFrames;

    public int Count => worker.Count;

    public ObjectDetectionService(ILogger<ObjectDetectionService> log, ObjectSettings settings, VideoSource videoSource, DetectionGate gate, AreaService area)
    {
        this.log = log;
        this.settings = settings;
        this.videoSource = videoSource;
        this.gate = gate;
        worker = new DetectionWorker<ObjectBox>("Object detection", MaxObjects, TimeSpan.FromMilliseconds(settings.Interval), Load, () => area.Current)
        {
            IsEnabled = settings.Enable
        };

        videoSource.FrameCaptured += OnFrameCaptured;
        videoSource.StateChanged += OnStateChanged;
    }

    public void Dispose()
    {
        videoSource.FrameCaptured -= OnFrameCaptured;
        videoSource.StateChanged -= OnStateChanged;
        worker.Dispose();
    }

    //--------------------------------------------------------------------------------
    // Hosted service
    //--------------------------------------------------------------------------------

    public Task StartAsync(CancellationToken cancellationToken)
    {
        worker.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        videoSource.FrameCaptured -= OnFrameCaptured;
        worker.Stop();
        return Task.CompletedTask;
    }

    //--------------------------------------------------------------------------------
    // Result
    //--------------------------------------------------------------------------------

    public DetectionInfo CopyResults(Span<ObjectBox> destination) => worker.CopyResults(destination);

    public LatencySnapshot TakePreprocessTime() => worker.TakePreprocessTime();

    public LatencySnapshot TakeInferenceTime() => worker.TakeInferenceTime();

    // The results of the last frame are not shown after the camera stops
    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (!videoSource.IsRunning)
        {
            worker.Clear();
        }
    }

    //--------------------------------------------------------------------------------
    // Detection thread
    //--------------------------------------------------------------------------------

    private ObjectDetector? Load()
    {
        try
        {
            var start = Stopwatch.GetTimestamp();
            var detector = new ObjectDetector(
                Path.Combine(AppContext.BaseDirectory, settings.Model),
                new ObjectDetectorOptions
                {
                    ConfidenceThreshold = settings.ConfidenceThreshold,
                    Classes = settings.GetClassIds(),
                    IntraOpNumThreads = settings.IntraOpNumThreads,
                    InterOpNumThreads = settings.InterOpNumThreads
                });
            log.InfoObjectDetectorLoaded(settings.Model, detector.InputWidth, detector.InputHeight, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            return detector;
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
        if (!worker.IsEnabled ||
            gate.IsIdle(worker.LastPreparedTimestamp) ||
            (worker.TryBeginPrepare() is not { } detector))
        {
            return;
        }

        var frame = e.Frame;
        var prepared = DetectionInput.TryPrepare(frame, detector.Input, detector.InputWidth, detector.InputHeight, ObjectDetector.Normalization);
        worker.EndPrepare(prepared, frame.Sequence, frame.Width, frame.Height);
    }
}
