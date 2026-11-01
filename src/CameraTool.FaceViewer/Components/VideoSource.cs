namespace CameraTool.FaceViewer.Components;

using System.Runtime.InteropServices;

using CameraTool.Capture;
using CameraTool.FaceViewer.Settings;

using Microsoft.Extensions.Hosting;

public readonly record struct FrameInfo(int Width, int Height, long Sequence, TimeSpan ArrivalTime);

public sealed class VideoSource : IHostedService, IDisposable
{
    private const int BytesPerPixel = 4;

    private const double FrameRateTolerance = 0.5;

    private const VideoPixelFormat OutputPixelFormat = VideoPixelFormat.Bgra8;

    private readonly ILogger<VideoSource> log;

    private readonly CameraSettings settings;

    private readonly SemaphoreSlim gate = new(1, 1);

    private readonly Lock frameSync = new();

    private readonly LatencyCounter captureLatency = new();

    private VideoCapture? capture;

    private Task? startupTask;

    private byte[] frameBuffer = [];

    private int frameWidth;

    private int frameHeight;

    private long frameSequence;

    private TimeSpan frameArrivalTime;

    private long capturedFrames;

    public event EventHandler? StateChanged;

    public event EventHandler? FrameUpdated;

    public IReadOnlyList<VideoDevice> Devices { get; private set; } = [];

    public VideoDevice? Device { get; private set; }

    public VideoFormat? Format { get; private set; }

    public VideoPixelFormat Output { get; private set; }

    public bool IsRunning { get; private set; }

    public string? ErrorMessage { get; private set; }

    public long CapturedFrames => Interlocked.Read(ref capturedFrames);

    public VideoSource(ILogger<VideoSource> log, CameraSettings settings)
    {
        this.log = log;
        this.settings = settings;
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
            await startupTask.ConfigureAwait(false);
        }

        await StopCaptureAsync().ConfigureAwait(false);
    }

    private async Task StartupAsync()
    {
        await RefreshAsync().ConfigureAwait(false);

        var device = String.IsNullOrEmpty(settings.Name)
            ? (Devices.Count > 0 ? Devices[0] : null)
            : Devices.FirstOrDefault(x => String.Equals(x.Name, settings.Name, StringComparison.OrdinalIgnoreCase));
        if (device is null)
        {
            log.WarnCameraNotFound(settings.Name);
            ErrorMessage = "Camera not found.";
            RaiseStateChanged();
            return;
        }

        if (FindPreferredFormat(device) is not { } format)
        {
            log.WarnCameraFormatNotFound(device.Name, String.Create(CultureInfo.InvariantCulture, $"{settings.Format} {settings.Width}x{settings.Height} {settings.Fps}fps"));
            ErrorMessage = "Camera format not supported.";
            RaiseStateChanged();
            return;
        }

        await StartCaptureAsync(device, format).ConfigureAwait(false);
    }

    //--------------------------------------------------------------------------------
    // Control
    //--------------------------------------------------------------------------------

    public VideoFormat? FindPreferredFormat(VideoDevice device)
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

    public async Task RefreshAsync()
    {
        try
        {
            Devices = await VideoDeviceCatalog.GetDevicesAsync().ConfigureAwait(false);
            log.InfoCameraDevices(Devices.Count);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            log.ErrorCameraEnumerationFailed(ex);
            ErrorMessage = ex.Message;
        }

        RaiseStateChanged();
    }

    public async Task StartCaptureAsync(VideoDevice device, VideoFormat format)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCaptureCoreAsync().ConfigureAwait(false);

            var video = new VideoCapture(device.Id);
            video.FrameCaptured += OnFrameCaptured;
            video.Failed += OnFailed;
            try
            {
                await video.OpenAsync(format, OutputPixelFormat).ConfigureAwait(false);
                await video.StartAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException)
            {
                video.FrameCaptured -= OnFrameCaptured;
                video.Failed -= OnFailed;
                await video.DisposeAsync().ConfigureAwait(false);

                log.ErrorCameraStartFailed(ex, device.Name, format.ToString());
                ErrorMessage = ex.Message;
                return;
            }

            capture = video;
            Device = device;
            Format = format;
            Output = video.OutputPixelFormat;
            IsRunning = true;
            ErrorMessage = null;
            log.InfoCameraStarted(device.Name, format.ToString(), Output.ToString());
        }
        finally
        {
            gate.Release();
            RaiseStateChanged();
        }
    }

    public async Task StopCaptureAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCaptureCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
            RaiseStateChanged();
        }
    }

    private async Task StopCaptureCoreAsync()
    {
        if (capture is null)
        {
            return;
        }

        capture.FrameCaptured -= OnFrameCaptured;
        capture.Failed -= OnFailed;
        await capture.DisposeAsync().ConfigureAwait(false);
        capture = null;
        IsRunning = false;

        log.InfoCameraStopped();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    //--------------------------------------------------------------------------------
    // Frame
    //--------------------------------------------------------------------------------

    public FrameInfo LatestFrame
    {
        get
        {
            lock (frameSync)
            {
                return new FrameInfo(frameWidth, frameHeight, frameSequence, frameArrivalTime);
            }
        }
    }

    public unsafe bool TryCopyFrame(IntPtr destination, int stride, int width, int height, out FrameInfo info)
    {
        lock (frameSync)
        {
            info = new FrameInfo(frameWidth, frameHeight, frameSequence, frameArrivalTime);
            if ((frameWidth != width) || (frameHeight != height) || (frameBuffer.Length == 0))
            {
                return false;
            }

            var rowBytes = width * BytesPerPixel;
            fixed (byte* source = frameBuffer)
            {
                if (stride == rowBytes)
                {
                    Buffer.MemoryCopy(source, (void*)destination, (long)stride * height, (long)rowBytes * height);
                }
                else
                {
                    for (var y = 0; y < height; y++)
                    {
                        Buffer.MemoryCopy(source + (y * rowBytes), (byte*)destination + (y * stride), stride, rowBytes);
                    }
                }
            }

            return true;
        }
    }

    public LatencySnapshot TakeCaptureLatency() => captureLatency.Take();

    private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
    {
        var frame = e.Frame;
        if (frame.PixelFormat != VideoPixelFormat.Bgra8)
        {
            return;
        }

        var plane = frame.Plane0;
        var rowBytes = frame.Width * BytesPerPixel;
        var source = frame.GetPlaneSpan(plane);
        if ((plane.Stride < rowBytes) || (source.Length < (plane.Stride * (frame.Height - 1)) + rowBytes))
        {
            return;
        }

        lock (frameSync)
        {
            var size = rowBytes * frame.Height;
            if (frameBuffer.Length != size)
            {
                frameBuffer = new byte[size];
            }

            if (plane.Stride == rowBytes)
            {
                source[..size].CopyTo(frameBuffer);
            }
            else
            {
                for (var y = 0; y < frame.Height; y++)
                {
                    source.Slice(y * plane.Stride, rowBytes).CopyTo(frameBuffer.AsSpan(y * rowBytes));
                }
            }

            frameWidth = frame.Width;
            frameHeight = frame.Height;
            frameSequence = frame.Sequence;
            frameArrivalTime = frame.ArrivalTime;
        }

        captureLatency.Add(frame.ArrivalTime - frame.CaptureTime);
        Interlocked.Increment(ref capturedFrames);
        FrameUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void OnFailed(object? sender, VideoCaptureFailedEventArgs e)
    {
        log.ErrorCameraFailed(e.Message, e.Code);
        ErrorMessage = e.Message;
        _ = StopCaptureAsync();
    }
}
