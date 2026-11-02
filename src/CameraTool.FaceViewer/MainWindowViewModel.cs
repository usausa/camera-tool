namespace CameraTool.FaceViewer;

using System.Diagnostics;
using System.Windows.Threading;

using CameraTool.Capture;
using CameraTool.FaceViewer.Components;
using CameraTool.FaceViewer.Controls;
using CameraTool.FaceViewer.Settings;
using CameraTool.Vision;

// ReSharper disable once ClassNeverInstantiated.Global
[ObservableGeneratorOption(Reactive = true, ViewModel = true)]
public sealed partial class MainWindowViewModel : ExtendViewModelBase
{
    private const double BytesPerKilobyte = 1024;

    private const int StatisticsLogInterval = 5;

    private const string MotionOff = "Off";

    private const string MotionMoving = "Moving";

    private const string MotionStill = "Still";

    private const string AreaFull = "Full";

    private static readonly TimeSpan StatisticsInterval = TimeSpan.FromSeconds(1);

    private readonly ILogger<MainWindowViewModel> log;

    private readonly TimeProvider timeProvider;

    private readonly VideoSource videoSource;

    private readonly TimeSpan motionHoldTime;

    private readonly Dispatcher dispatcher;

    private readonly DispatcherTimer statisticsTimer;

    private int statisticsCount;

    private long lastStatisticsTimestamp;

    private long lastCapturedFrames;

    private long lastDisplayedFrames;

    private long lastDetectedFrames;

    private TimeSpan lastProcessorTime;

    private long lastAllocatedBytes;

    private int lastGc0Count;

    private int lastGc1Count;

    private int lastGc2Count;

    public FrameStore Frames => videoSource.Frames;

    public FaceDetectionService FaceDetection { get; }

    public MotionService Motion { get; }

    public AreaService Area { get; }

    public IReadOnlyList<FrameConversion> Conversions { get; } = Enum.GetValues<FrameConversion>();

    public IReadOnlyList<VideoUpdateMode> UpdateModes { get; } = Enum.GetValues<VideoUpdateMode>();

    [ObservableProperty]
    public partial IReadOnlyList<VideoDevice> Devices { get; set; } = [];

    [ObservableProperty]
    public partial VideoDevice? SelectedDevice { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<VideoFormat> Formats { get; set; } = [];

    [ObservableProperty]
    public partial VideoFormat? SelectedFormat { get; set; }

    [ObservableProperty]
    public partial FrameConversion SelectedConversion { get; set; }

    [ObservableProperty]
    public partial VideoUpdateMode SelectedUpdateMode { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string? RunningFormat { get; set; }

    [ObservableProperty]
    public partial string? RunningOutput { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    // Size of the running frames (0 while stopped)
    [ObservableProperty]
    public partial int FrameWidth { get; set; }

    [ObservableProperty]
    public partial int FrameHeight { get; set; }

    [ObservableProperty]
    public partial double CaptureFps { get; set; }

    [ObservableProperty]
    public partial double DisplayFps { get; set; }

    [ObservableProperty]
    public partial double CaptureLatency { get; set; }

    [ObservableProperty]
    public partial double CaptureLatencyMax { get; set; }

    [ObservableProperty]
    public partial double ProcessTime { get; set; }

    [ObservableProperty]
    public partial double ProcessTimeMax { get; set; }

    [ObservableProperty]
    public partial double DisplayLatency { get; set; }

    [ObservableProperty]
    public partial double DisplayLatencyMax { get; set; }

    [ObservableProperty]
    public partial double CpuUsage { get; set; }

    [ObservableProperty]
    public partial double AllocatedPerSec { get; set; }

    [ObservableProperty]
    public partial double Gc0PerSec { get; set; }

    [ObservableProperty]
    public partial double Gc1PerSec { get; set; }

    [ObservableProperty]
    public partial double Gc2PerSec { get; set; }

    [ObservableProperty]
    public partial bool IsDetectionEnabled { get; set; }

    [ObservableProperty]
    public partial string? DetectionError { get; set; }

    [ObservableProperty]
    public partial double DetectFps { get; set; }

    [ObservableProperty]
    public partial double PreprocessTime { get; set; }

    [ObservableProperty]
    public partial double PreprocessTimeMax { get; set; }

    [ObservableProperty]
    public partial double InferenceTime { get; set; }

    [ObservableProperty]
    public partial double InferenceTimeMax { get; set; }

    [ObservableProperty]
    public partial int FaceCount { get; set; }

    [ObservableProperty]
    public partial bool IsMotionEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsMotionGateEnabled { get; set; }

    [ObservableProperty]
    public partial string MotionStatus { get; set; } = MotionOff;

    [ObservableProperty]
    public partial double MotionLevel { get; set; }

    [ObservableProperty]
    public partial double MotionTime { get; set; }

    [ObservableProperty]
    public partial double MotionTimeMax { get; set; }

    [ObservableProperty]
    public partial bool IsAreaEditing { get; set; }

    [ObservableProperty]
    public partial string AreaStatus { get; set; } = AreaFull;

    public IObserveCommand StartCommand { get; }

    public IObserveCommand StopCommand { get; }

    public IObserveCommand RefreshCommand { get; }

    public IObserveCommand ResetAreaCommand { get; }

    public MainWindowViewModel(
        ILogger<MainWindowViewModel> log,
        TimeProvider timeProvider,
        DisplaySettings displaySettings,
        DetectSettings detectSettings,
        VideoSource videoSource,
        FaceDetectionService faceDetection,
        MotionService motion,
        AreaService area)
    {
        this.log = log;
        this.timeProvider = timeProvider;
        this.videoSource = videoSource;
        motionHoldTime = TimeSpan.FromMilliseconds(detectSettings.MotionHoldTime);
        FaceDetection = faceDetection;
        Motion = motion;
        Area = area;
        dispatcher = Application.Current.Dispatcher;
        SelectedConversion = videoSource.Conversion;
        SelectedUpdateMode = displaySettings.UpdateMode;
        IsDetectionEnabled = faceDetection.IsEnabled;
        IsMotionEnabled = motion.IsEnabled;
        IsMotionGateEnabled = faceDetection.IsMotionGateEnabled;

        StartCommand = MakeAsyncCommand(StartAsync, () => !IsRunning && (SelectedDevice is not null) && (SelectedFormat is not null));
        StopCommand = MakeAsyncCommand(videoSource.StopCaptureAsync, () => IsRunning);
        RefreshCommand = MakeAsyncCommand(videoSource.RefreshAsync, () => !IsRunning);
        ResetAreaCommand = MakeDelegateCommand(ResetArea);

        SubscribeSelectedDevice(x =>
        {
            Formats = x?.Formats ?? [];
            SelectedFormat = x is null ? null : videoSource.FindPreferredFormat(x);
        });
        SubscribeIsDetectionEnabled(x => faceDetection.IsEnabled = x);
        SubscribeIsMotionEnabled(x => motion.IsEnabled = x);
        SubscribeIsMotionGateEnabled(x => faceDetection.IsMotionGateEnabled = x);

        videoSource.StateChanged += OnStateChanged;
        area.Changed += OnAreaChanged;

        statisticsTimer = new DispatcherTimer { Interval = StatisticsInterval };
        statisticsTimer.Tick += OnStatisticsTick;
        ResetStatistics();
        statisticsTimer.Start();

        ApplyState();
        UpdateAreaStatus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            statisticsTimer.Stop();
            statisticsTimer.Tick -= OnStatisticsTick;
            videoSource.StateChanged -= OnStateChanged;
            Area.Changed -= OnAreaChanged;
        }

        base.Dispose(disposing);
    }

    //--------------------------------------------------------------------------------
    // Command
    //--------------------------------------------------------------------------------

    private Task StartAsync() =>
        (SelectedDevice is { } device) && (SelectedFormat is { } format) ? videoSource.StartCaptureAsync(device, format, SelectedConversion) : Task.CompletedTask;

    private void ResetArea()
    {
        Area.Update(DetectionArea.Full);
        Area.Save();
    }

    //--------------------------------------------------------------------------------
    // State
    //--------------------------------------------------------------------------------

    private void OnStateChanged(object? sender, EventArgs e) => dispatcher.InvokeAsync(ApplyState);

    private void ApplyState()
    {
        var deviceId = videoSource.Device?.Id ?? SelectedDevice?.Id;
        if (!ReferenceEquals(Devices, videoSource.Devices))
        {
            Devices = videoSource.Devices;
        }

        var device = Devices.FirstOrDefault(x => x.Id == deviceId) ?? (Devices.Count > 0 ? Devices[0] : null);
        if (!ReferenceEquals(device, SelectedDevice))
        {
            SelectedDevice = device;
        }

        if (videoSource.IsRunning && (videoSource.Format is { } format))
        {
            SelectedFormat = format;
            SelectedConversion = videoSource.Conversion;
            RunningFormat = format.ToString();
            RunningOutput = String.Create(CultureInfo.InvariantCulture, $"{videoSource.Output.ToString().ToUpperInvariant()} ({videoSource.Conversion})");
            FrameWidth = format.Width;
            FrameHeight = format.Height;
        }
        else
        {
            RunningFormat = null;
            RunningOutput = null;
            FrameWidth = 0;
            FrameHeight = 0;
            IsAreaEditing = false;
        }

        IsRunning = videoSource.IsRunning;
        ErrorMessage = videoSource.ErrorMessage;
    }

    //--------------------------------------------------------------------------------
    // Area
    //--------------------------------------------------------------------------------

    // UI thread
    private void OnAreaChanged(object? sender, EventArgs e) => UpdateAreaStatus();

    private void UpdateAreaStatus()
    {
        var current = Area.Current;
        AreaStatus = current.IsFull ? AreaFull : String.Create(CultureInfo.InvariantCulture, $"{current.CellCount} / {MotionDetector.CellCount} cells");
    }

    //--------------------------------------------------------------------------------
    // Statistics
    //--------------------------------------------------------------------------------

    private void ResetStatistics()
    {
        lastStatisticsTimestamp = timeProvider.GetTimestamp();
        lastCapturedFrames = videoSource.CapturedFrames;
        lastDisplayedFrames = videoSource.Frames.DisplayedFrames;
        lastDetectedFrames = FaceDetection.DetectedFrames;
        lastProcessorTime = GetProcessorTime();
        lastAllocatedBytes = GC.GetTotalAllocatedBytes();
        lastGc0Count = GC.CollectionCount(0);
        lastGc1Count = GC.CollectionCount(1);
        lastGc2Count = GC.CollectionCount(2);
    }

    private void OnStatisticsTick(object? sender, EventArgs e)
    {
        var timestamp = timeProvider.GetTimestamp();
        var elapsed = timeProvider.GetElapsedTime(lastStatisticsTimestamp, timestamp).TotalSeconds;
        if (elapsed <= 0)
        {
            return;
        }

        var capturedFrames = videoSource.CapturedFrames;
        var displayedFrames = videoSource.Frames.DisplayedFrames;
        CaptureFps = (capturedFrames - lastCapturedFrames) / elapsed;
        DisplayFps = (displayedFrames - lastDisplayedFrames) / elapsed;

        var capture = videoSource.TakeCaptureLatency();
        CaptureLatency = capture.Average.TotalMilliseconds;
        CaptureLatencyMax = capture.Max.TotalMilliseconds;
        var process = videoSource.TakeProcessTime();
        ProcessTime = process.Average.TotalMilliseconds;
        ProcessTimeMax = process.Max.TotalMilliseconds;
        var display = videoSource.Frames.TakeDisplayLatency();
        DisplayLatency = display.Average.TotalMilliseconds;
        DisplayLatencyMax = display.Max.TotalMilliseconds;

        var detectedFrames = FaceDetection.DetectedFrames;
        DetectFps = (detectedFrames - lastDetectedFrames) / elapsed;
        var preprocess = FaceDetection.TakePreprocessTime();
        PreprocessTime = preprocess.Average.TotalMilliseconds;
        PreprocessTimeMax = preprocess.Max.TotalMilliseconds;
        var inference = FaceDetection.TakeInferenceTime();
        InferenceTime = inference.Average.TotalMilliseconds;
        InferenceTimeMax = inference.Max.TotalMilliseconds;
        FaceCount = FaceDetection.FaceCount;
        DetectionError = FaceDetection.ErrorMessage;

        MotionStatus = !Motion.IsEnabled ? MotionOff : Motion.IsMoving(motionHoldTime) ? MotionMoving : MotionStill;
        MotionLevel = Motion.Info.Level * 100;
        var motionTime = Motion.TakeProcessTime();
        MotionTime = motionTime.Average.TotalMilliseconds;
        MotionTimeMax = motionTime.Max.TotalMilliseconds;

        var processorTime = GetProcessorTime();
        CpuUsage = (processorTime - lastProcessorTime).TotalSeconds / elapsed / Environment.ProcessorCount * 100;

        var allocatedBytes = GC.GetTotalAllocatedBytes();
        AllocatedPerSec = (allocatedBytes - lastAllocatedBytes) / BytesPerKilobyte / elapsed;

        var gc0Count = GC.CollectionCount(0);
        var gc1Count = GC.CollectionCount(1);
        var gc2Count = GC.CollectionCount(2);
        Gc0PerSec = (gc0Count - lastGc0Count) / elapsed;
        Gc1PerSec = (gc1Count - lastGc1Count) / elapsed;
        Gc2PerSec = (gc2Count - lastGc2Count) / elapsed;

        lastStatisticsTimestamp = timestamp;
        lastCapturedFrames = capturedFrames;
        lastDisplayedFrames = displayedFrames;
        lastDetectedFrames = detectedFrames;
        lastProcessorTime = processorTime;
        lastAllocatedBytes = allocatedBytes;
        lastGc0Count = gc0Count;
        lastGc1Count = gc1Count;
        lastGc2Count = gc2Count;

        statisticsCount++;
        if (IsRunning && (statisticsCount % StatisticsLogInterval == 0))
        {
            log.DebugStatistics(
                RunningFormat ?? string.Empty,
                RunningOutput ?? string.Empty,
                SelectedUpdateMode.ToString(),
                CaptureFps,
                DisplayFps,
                CaptureLatency,
                CaptureLatencyMax,
                ProcessTime,
                ProcessTimeMax,
                DisplayLatency,
                DisplayLatencyMax,
                CpuUsage,
                AllocatedPerSec,
                Gc0PerSec,
                Gc1PerSec,
                Gc2PerSec);
            log.DebugDetectionStatistics(
                IsDetectionEnabled,
                DetectFps,
                PreprocessTime,
                PreprocessTimeMax,
                InferenceTime,
                InferenceTimeMax,
                FaceCount);
            log.DebugMotionStatistics(
                IsMotionEnabled,
                IsMotionGateEnabled,
                MotionStatus,
                MotionLevel,
                MotionTime,
                MotionTimeMax);
        }
    }

    private static TimeSpan GetProcessorTime()
    {
        using var process = Process.GetCurrentProcess();
        return process.TotalProcessorTime;
    }
}
