namespace CameraTool.FaceViewer.Services;

using System.Diagnostics;

using CameraTool.Capture;
using CameraTool.FaceViewer.Settings;
using CameraTool.Vision;

using Microsoft.Extensions.Hosting;
using Microsoft.ML.OnnxRuntime;

// Detects faces (DetectionWorker: on a dedicated thread without blocking the capture).
// With the motion gate, the input is prepared only once per idle interval while there is no motion.
// Faces whose center is outside the detection area are removed from the results.
public sealed class FaceDetectionService : IHostedService, IDisposable, IDetectionStatistics
{
    public const int MaxFaces = 32;

    private readonly ILogger<FaceDetectionService> log;

    private readonly DetectSettings settings;

    private readonly VideoSource videoSource;

    private readonly DetectionGate gate;

    private readonly DetectionWorker<FaceBox> worker;

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

    // DetectionGate depends on MotionService, so MotionService subscribes to the frames first
    public FaceDetectionService(ILogger<FaceDetectionService> log, DetectSettings settings, VideoSource videoSource, DetectionGate gate, AreaService area)
    {
        this.log = log;
        this.settings = settings;
        this.videoSource = videoSource;
        this.gate = gate;
        worker = new DetectionWorker<FaceBox>("Face detection", MaxFaces, TimeSpan.Zero, Load, () => area.Current)
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

    public DetectionInfo CopyResults(Span<FaceBox> destination) => worker.CopyResults(destination);

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
        if (!worker.IsEnabled ||
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
