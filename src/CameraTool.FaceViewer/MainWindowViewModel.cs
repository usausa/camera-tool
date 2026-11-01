namespace CameraTool.FaceViewer;

using System.Windows.Media.Imaging;
using System.Windows.Threading;

using CameraTool.Capture;
using CameraTool.FaceViewer.Components;

// ReSharper disable once ClassNeverInstantiated.Global
[ObservableGeneratorOption(Reactive = true, ViewModel = true)]
public sealed partial class MainWindowViewModel : ExtendViewModelBase
{
    private const double Dpi = 96;

    private const double BytesPerKilobyte = 1024;

    private static readonly TimeSpan StatisticsInterval = TimeSpan.FromSeconds(1);

    private readonly TimeProvider timeProvider;

    private readonly VideoSource videoSource;

    private readonly Dispatcher dispatcher;

    private readonly DispatcherTimer statisticsTimer;

    private readonly Action updateFrameAction;

    private readonly LatencyCounter displayLatency = new();

    private WriteableBitmap? bitmap;

    private int frameUpdating;

    private long lastSequence;

    private int displayedFrames;

    private long lastStatisticsTimestamp;

    private long lastCapturedFrames;

    private long lastAllocatedBytes;

    private int lastGc0Count;

    private int lastGc1Count;

    private int lastGc2Count;

    [ObservableProperty]
    public partial WriteableBitmap? Bitmap { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<VideoDevice> Devices { get; set; } = [];

    [ObservableProperty]
    public partial VideoDevice? SelectedDevice { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<VideoFormat> Formats { get; set; } = [];

    [ObservableProperty]
    public partial VideoFormat? SelectedFormat { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string? RunningFormat { get; set; }

    [ObservableProperty]
    public partial string? RunningOutput { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial double CaptureFps { get; set; }

    [ObservableProperty]
    public partial double DisplayFps { get; set; }

    [ObservableProperty]
    public partial double CaptureLatency { get; set; }

    [ObservableProperty]
    public partial double CaptureLatencyMax { get; set; }

    [ObservableProperty]
    public partial double DisplayLatency { get; set; }

    [ObservableProperty]
    public partial double DisplayLatencyMax { get; set; }

    [ObservableProperty]
    public partial double AllocatedPerSec { get; set; }

    [ObservableProperty]
    public partial double Gc0PerSec { get; set; }

    [ObservableProperty]
    public partial double Gc1PerSec { get; set; }

    [ObservableProperty]
    public partial double Gc2PerSec { get; set; }

    public IObserveCommand StartCommand { get; }

    public IObserveCommand StopCommand { get; }

    public IObserveCommand RefreshCommand { get; }

    public MainWindowViewModel(TimeProvider timeProvider, VideoSource videoSource)
    {
        this.timeProvider = timeProvider;
        this.videoSource = videoSource;
        dispatcher = Application.Current.Dispatcher;
        updateFrameAction = UpdateFrame;

        StartCommand = MakeAsyncCommand(StartAsync, () => !IsRunning && (SelectedDevice is not null) && (SelectedFormat is not null));
        StopCommand = MakeAsyncCommand(videoSource.StopCaptureAsync, () => IsRunning);
        RefreshCommand = MakeAsyncCommand(videoSource.RefreshAsync, () => !IsRunning);

        SubscribeSelectedDevice(x =>
        {
            Formats = x?.Formats ?? [];
            SelectedFormat = x is null ? null : videoSource.FindPreferredFormat(x);
        });

        videoSource.StateChanged += OnStateChanged;
        videoSource.FrameUpdated += OnFrameUpdated;

        statisticsTimer = new DispatcherTimer { Interval = StatisticsInterval };
        statisticsTimer.Tick += OnStatisticsTick;
        ResetStatistics();
        statisticsTimer.Start();

        ApplyState();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            statisticsTimer.Stop();
            statisticsTimer.Tick -= OnStatisticsTick;
            videoSource.StateChanged -= OnStateChanged;
            videoSource.FrameUpdated -= OnFrameUpdated;
        }

        base.Dispose(disposing);
    }

    //--------------------------------------------------------------------------------
    // Command
    //--------------------------------------------------------------------------------

    private Task StartAsync() =>
        (SelectedDevice is { } device) && (SelectedFormat is { } format) ? videoSource.StartCaptureAsync(device, format) : Task.CompletedTask;

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
            RunningFormat = format.ToString();
            RunningOutput = videoSource.Output.ToString().ToUpperInvariant();
        }
        else
        {
            RunningFormat = null;
            RunningOutput = null;
        }

        IsRunning = videoSource.IsRunning;
        ErrorMessage = videoSource.ErrorMessage;
    }

    //--------------------------------------------------------------------------------
    // Frame
    //--------------------------------------------------------------------------------

    private void OnFrameUpdated(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref frameUpdating, 1) == 0)
        {
            dispatcher.InvokeAsync(updateFrameAction, DispatcherPriority.Render);
        }
    }

    private void UpdateFrame()
    {
        Interlocked.Exchange(ref frameUpdating, 0);

        var info = videoSource.LatestFrame;
        if ((info.Width <= 0) || (info.Height <= 0) || (info.Sequence == lastSequence))
        {
            return;
        }

        if ((bitmap is null) || (bitmap.PixelWidth != info.Width) || (bitmap.PixelHeight != info.Height))
        {
            bitmap = new WriteableBitmap(info.Width, info.Height, Dpi, Dpi, PixelFormats.Bgr32, null);
            Bitmap = bitmap;
        }

        bitmap.Lock();
        try
        {
            if (videoSource.TryCopyFrame(bitmap.BackBuffer, bitmap.BackBufferStride, bitmap.PixelWidth, bitmap.PixelHeight, out var copied))
            {
                bitmap.AddDirtyRect(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
                lastSequence = copied.Sequence;
                displayedFrames++;
                displayLatency.Add(CaptureClock.Now - copied.ArrivalTime);
            }
        }
        finally
        {
            bitmap.Unlock();
        }
    }

    //--------------------------------------------------------------------------------
    // Statistics
    //--------------------------------------------------------------------------------

    private void ResetStatistics()
    {
        lastStatisticsTimestamp = timeProvider.GetTimestamp();
        lastCapturedFrames = videoSource.CapturedFrames;
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
        CaptureFps = (capturedFrames - lastCapturedFrames) / elapsed;
        DisplayFps = displayedFrames / elapsed;
        displayedFrames = 0;

        var capture = videoSource.TakeCaptureLatency();
        CaptureLatency = capture.Average.TotalMilliseconds;
        CaptureLatencyMax = capture.Max.TotalMilliseconds;
        var display = displayLatency.Take();
        DisplayLatency = display.Average.TotalMilliseconds;
        DisplayLatencyMax = display.Max.TotalMilliseconds;

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
        lastAllocatedBytes = allocatedBytes;
        lastGc0Count = gc0Count;
        lastGc1Count = gc1Count;
        lastGc2Count = gc2Count;
    }
}
