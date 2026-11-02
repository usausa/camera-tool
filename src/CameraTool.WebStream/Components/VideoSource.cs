namespace CameraTool.WebStream.Components;

using System.Runtime.InteropServices;

using CameraTool.Capture;
using CameraTool.WebStream.Settings;

public sealed class VideoSource : IHostedService, IDisposable
{
    private const double FrameRateTolerance = 0.5;

    private readonly ILogger<VideoSource> log;

    private readonly CameraSettings settings;

    private readonly ICameraProvider cameras;

    private readonly SemaphoreSlim gate = new(1, 1);

    private readonly LatencyCounter captureLatency = new();

    private IVideoCapture? capture;

    private Task? startupTask;

    private long capturedFrames;

    // Raised on the capture thread, the frame is valid only while the handler runs
    public event EventHandler<FrameCapturedEventArgs>? FrameCaptured;

    public VideoDevice? Device { get; private set; }

    public VideoFormat? Format { get; private set; }

    public bool IsRunning { get; private set; }

    public string? ErrorMessage { get; private set; }

    public long CapturedFrames => Interlocked.Read(ref capturedFrames);

    public VideoSource(ILogger<VideoSource> log, CameraSettings settings, ICameraProvider cameras)
    {
        this.log = log;
        this.settings = settings;
        this.cameras = cameras;
    }

    public void Dispose()
    {
        capture?.Dispose();
        capture = null;
        gate.Dispose();
    }

    //--------------------------------------------------------------------------------
    // Hosted service
    //--------------------------------------------------------------------------------

    public Task StartAsync(CancellationToken cancellationToken)
    {
        startupTask = StartupAsync();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (startupTask is not null)
        {
            await startupTask;
        }

        await StopCaptureAsync();
    }

    private async Task StartupAsync()
    {
        IReadOnlyList<VideoDevice> devices;
        try
        {
            devices = await cameras.GetDevicesAsync();
            log.InfoCameraDevices(devices.Count);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            log.ErrorCameraEnumerationFailed(ex);
            ErrorMessage = ex.Message;
            return;
        }

        var device = String.IsNullOrEmpty(settings.Name)
            ? (devices.Count > 0 ? devices[0] : null)
            : devices.FirstOrDefault(x => String.Equals(x.Name, settings.Name, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            log.WarnCameraNotFound(settings.Name);
            ErrorMessage = "Camera not found.";
            return;
        }

        if (FindPreferredFormat(device) is not { } format)
        {
            log.WarnCameraFormatNotFound(device.Name, String.Create(CultureInfo.InvariantCulture, $"{settings.Format} {settings.Width}x{settings.Height} {settings.Fps}fps"));
            ErrorMessage = "Camera format not supported.";
            return;
        }

        await StartCaptureAsync(device, format);
    }

    //--------------------------------------------------------------------------------
    // Control
    //--------------------------------------------------------------------------------

    private VideoFormat? FindPreferredFormat(VideoDevice device)
    {
        foreach (var format in device.Formats)
        {
            if ((format.PixelFormat == settings.Format) &&
                (format.Width == settings.Width) &&
                (format.Height == settings.Height) &&
                (Math.Abs(format.FrameRate - settings.Fps) < FrameRateTolerance))
            {
                return format;
            }
        }

        return null;
    }

    private async Task StartCaptureAsync(VideoDevice device, VideoFormat format)
    {
        await gate.WaitAsync();
        try
        {
            // Native output, converted by the consumer
            var video = cameras.CreateCapture(device.Id);
            video.FrameCaptured += OnFrameCaptured;
            video.Failed += OnFailed;
            try
            {
                await video.OpenAsync(format);
                await video.StartAsync();
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException)
            {
                video.FrameCaptured -= OnFrameCaptured;
                video.Failed -= OnFailed;
                await video.DisposeAsync();

                log.ErrorCameraStartFailed(ex, device.Name, format.ToString());
                ErrorMessage = ex.Message;
                return;
            }

            capture = video;
            Device = device;
            Format = format;
            IsRunning = true;
            ErrorMessage = null;
            log.InfoCameraStarted(device.Name, format.ToString(), video.OutputPixelFormat.ToString());
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task StopCaptureAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (capture is null)
            {
                return;
            }

            capture.FrameCaptured -= OnFrameCaptured;
            capture.Failed -= OnFailed;
            await capture.DisposeAsync();
            capture = null;
            IsRunning = false;

            log.InfoCameraStopped();
        }
        finally
        {
            gate.Release();
        }
    }

    //--------------------------------------------------------------------------------
    // Statistics
    //--------------------------------------------------------------------------------

    public LatencySnapshot TakeCaptureLatency() => captureLatency.Take();

    //--------------------------------------------------------------------------------
    // Frame
    //--------------------------------------------------------------------------------

    private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
    {
        var frame = e.Frame;
        captureLatency.Add(frame.ArrivalTime - frame.CaptureTime);
        Interlocked.Increment(ref capturedFrames);

        FrameCaptured?.Invoke(this, e);
    }

    private void OnFailed(object? sender, VideoCaptureFailedEventArgs e)
    {
        log.ErrorCameraFailed(e.Message, e.Code);
        ErrorMessage = e.Message;
        _ = StopCaptureAsync();
    }
}
