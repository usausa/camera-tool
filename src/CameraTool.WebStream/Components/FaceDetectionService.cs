namespace CameraTool.WebStream.Components;

using System.Diagnostics;

using CameraTool.Capture;
using CameraTool.Vision;
using CameraTool.WebStream.Settings;

using Microsoft.ML.OnnxRuntime;

// Detects faces while clients are connected (DetectionWorker: on a dedicated thread without blocking the capture).
// With the motion gate, the input is prepared only once per idle interval while there is no motion.
// Faces whose center is outside the detection area are removed from the results.
public sealed class FaceDetectionService : IHostedService, IDisposable, IDetectionStatistics
{
    public const int MaxFaces = 32;

    private readonly ILogger<FaceDetectionService> log;

    private readonly DetectSettings settings;

    private readonly VideoSource source;

    private readonly StreamHubs hubs;

    private readonly DetectionGate gate;

    private readonly DetectionWorker<FaceBox> worker;

    public bool IsEnabled
    {
        get => worker.IsEnabled;
        set
        {
            worker.IsEnabled = value;
            log.InfoFaceDetectionChanged(value);
        }
    }

    public bool IsAvailable => worker.IsAvailable;

    // Set on the detection thread when the model cannot be loaded
    public string? ErrorMessage { get; private set; }

    public long DetectedFrames => worker.DetectedFrames;

    public int Count => worker.Count;

    public FaceDetectionService(ILogger<FaceDetectionService> log, DetectSettings settings, VideoSource source, StreamHubs hubs, DetectionGate gate, AreaService area)
    {
        this.log = log;
        this.settings = settings;
        this.source = source;
        this.hubs = hubs;
        this.gate = gate;
        worker = new DetectionWorker<FaceBox>("Face detection", MaxFaces, TimeSpan.Zero, Load, () => area.Current)
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

    public DetectionInfo CopyResults(Span<FaceBox> destination) => worker.CopyResults(destination);

    public LatencySnapshot TakePreprocessTime() => worker.TakePreprocessTime();

    public LatencySnapshot TakeInferenceTime() => worker.TakeInferenceTime();

    //--------------------------------------------------------------------------------
    // Detection thread
    //--------------------------------------------------------------------------------

    private FaceDetector? Load()
    {
        try
        {
            var start = Stopwatch.GetTimestamp();
            var detector = new FaceDetector(
                Path.Combine(AppContext.BaseDirectory, settings.Model),
                new FaceDetectorOptions
                {
                    ConfidenceThreshold = settings.ConfidenceThreshold,
                    IouThreshold = settings.IouThreshold,
                    IntraOpNumThreads = settings.IntraOpNumThreads,
                    InterOpNumThreads = settings.InterOpNumThreads
                });
            log.InfoFaceDetectorLoaded(settings.Model, detector.InputWidth, detector.InputHeight, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            return detector;
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or IOException or InvalidOperationException or DllNotFoundException or TypeInitializationException)
        {
            log.ErrorFaceDetectorLoadFailed(ex, settings.Model);
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
        if (!worker.IsEnabled ||
            !hubs.HasClients ||
            gate.IsIdle(worker.LastPreparedTimestamp) ||
            (worker.TryBeginPrepare() is not { } detector))
        {
            return;
        }

        var frame = e.Frame;
        var prepared = DetectionInput.TryPrepare(frame, detector.Input, detector.InputWidth, detector.InputHeight, FaceDetector.Normalization);
        worker.EndPrepare(prepared, frame.Sequence, frame.Width, frame.Height);
    }
}
