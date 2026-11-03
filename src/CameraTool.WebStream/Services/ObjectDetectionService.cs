namespace CameraTool.WebStream.Services;

using System.Diagnostics;

using CameraTool.Capture;
using CameraTool.Vision;
using CameraTool.WebStream.Settings;

using Microsoft.ML.OnnxRuntime;

// Detects objects (COCO classes) while clients are connected, in the same way as FaceDetectionService.
// The inference takes about 100 ms, so the input is prepared at most once per interval
// (and once per idle interval while the gate waits for motion). The model is loaded when the detection is enabled first.
// Objects whose center is outside the detection area are removed from the results.
public sealed class ObjectDetectionService : IHostedService, IDisposable, IDetectionStatistics
{
    public const int MaxObjects = 32;

    private readonly ILogger<ObjectDetectionService> log;

    private readonly ObjectSettings settings;

    private readonly VideoSource source;

    private readonly StreamHubs hubs;

    private readonly DetectionWorker<ObjectBox> worker;

    public bool IsEnabled
    {
        get => worker.IsEnabled;
        set
        {
            worker.IsEnabled = value;
            log.InfoObjectDetectionChanged(value);
        }
    }

    public bool IsAvailable => worker.IsAvailable;

    // Set on the detection thread when the model cannot be loaded
    public string? ErrorMessage { get; private set; }

    public long DetectedFrames => worker.DetectedFrames;

    public int Count => worker.Count;

    public ObjectDetectionService(ILogger<ObjectDetectionService> log, ObjectSettings settings, VideoSource source, StreamHubs hubs, DetectionGate gate, AreaService area)
    {
        this.log = log;
        this.settings = settings;
        this.source = source;
        this.hubs = hubs;
        worker = new DetectionWorker<ObjectBox>("Object detection", MaxObjects, TimeSpan.FromMilliseconds(settings.Interval), gate.IsIdle, Load, () => area.Current)
        {
            IsEnabled = settings.Enable
        };
    }

    public void Dispose() => worker.Dispose();

    //--------------------------------------------------------------------------------
    // Hosted service
    //--------------------------------------------------------------------------------

    public Task StartAsync(CancellationToken cancellationToken)
    {
        source.FrameCaptured += OnFrameCaptured;
        worker.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        source.FrameCaptured -= OnFrameCaptured;
        worker.Stop();
        return Task.CompletedTask;
    }

    //--------------------------------------------------------------------------------
    // Result
    //--------------------------------------------------------------------------------

    public DetectionInfo CopyResults(Span<ObjectBox> destination) => worker.CopyResults(destination);

    public LatencySnapshot TakePreprocessTime() => worker.TakePreprocessTime();

    public LatencySnapshot TakeInferenceTime() => worker.TakeInferenceTime();

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
        // The results are used only by the stream
        if (!hubs.HasClients || (worker.TryBeginPrepare() is not { } detector))
        {
            return;
        }

        var frame = e.Frame;
        var prepared = DetectionInput.TryPrepare(frame, detector.Input, detector.InputWidth, detector.InputHeight, ObjectDetector.Normalization);
        worker.EndPrepare(prepared, frame.Sequence, frame.Width, frame.Height);
    }
}
