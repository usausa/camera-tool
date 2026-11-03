namespace CameraTool.FaceViewer.Services;

using System.Diagnostics;
using System.Runtime.InteropServices;

using CameraTool.Capture;
using CameraTool.FaceViewer.Settings;
using CameraTool.Vision;

using Microsoft.Extensions.Hosting;

public sealed class VideoSource : IHostedService, IDisposable
{
    private const int BytesPerPixel = 4;

    private const double FrameRateTolerance = 0.5;

    private readonly ILogger<VideoSource> log;

    private readonly CameraSettings settings;

    private readonly SemaphoreSlim gate = new(1, 1);

    private readonly LatencyCounter captureLatency = new();

    private readonly LatencyCounter processTime = new();

    private VideoCapture? capture;

    private Task? startupTask;

    private long capturedFrames;

    public event EventHandler? StateChanged;

    // Raised on the capture thread after the frame is stored, the frame is valid only while the handler runs
    public event EventHandler<FrameCapturedEventArgs>? FrameCaptured;

    public FrameStore Frames { get; } = new();

    public IReadOnlyList<VideoDevice> Devices { get; private set; } = [];

    public VideoDevice? Device { get; private set; }

    public VideoFormat? Format { get; private set; }

    public FrameConversion Conversion { get; private set; }

    public VideoPixelFormat Output { get; private set; }

    public bool IsRunning { get; private set; }

    public string? ErrorMessage { get; private set; }

    public long CapturedFrames => Interlocked.Read(ref capturedFrames);

    public VideoSource(ILogger<VideoSource> log, CameraSettings settings)
    {
        this.log = log;
        this.settings = settings;
        Conversion = settings.Conversion;
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

        await StartCaptureAsync(device, format, settings.Conversion).ConfigureAwait(false);
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

    public async Task StartCaptureAsync(VideoDevice device, VideoFormat format, FrameConversion conversion)
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
                await video.OpenAsync(format, conversion == FrameConversion.Reader ? VideoPixelFormat.Bgra8 : VideoPixelFormat.Unknown).ConfigureAwait(false);
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
            Conversion = conversion;
            Output = video.OutputPixelFormat;
            IsRunning = true;
            ErrorMessage = null;
            log.InfoCameraStarted(device.Name, format.ToString(), Output.ToString(), conversion.ToString());
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
    // Statistics
    //--------------------------------------------------------------------------------

    public LatencySnapshot TakeCaptureLatency() => captureLatency.Take();

    public LatencySnapshot TakeProcessTime() => processTime.Take();

    //--------------------------------------------------------------------------------
    // Frame
    //--------------------------------------------------------------------------------

    private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
    {
        var start = Stopwatch.GetTimestamp();
        var frame = e.Frame;
        var slot = Frames.BeginWrite(frame.Width, frame.Height);
        if (!WriteFrame(frame, slot))
        {
            return;
        }

        slot.Sequence = frame.Sequence;
        slot.CaptureTime = frame.CaptureTime;
        slot.ArrivalTime = frame.ArrivalTime;
        Frames.EndWrite();

        processTime.Add(Stopwatch.GetElapsedTime(start));
        captureLatency.Add(frame.ArrivalTime - frame.CaptureTime);
        Interlocked.Increment(ref capturedFrames);

        FrameCaptured?.Invoke(this, e);
    }

    private static bool WriteFrame(in FrameBuffer frame, FrameSlot slot)
    {
        var destination = slot.Data;
        // YUV without range information is treated as video range
        var range = frame.ColorRange == VideoColorRange.Full ? YuvRange.Full : YuvRange.Limited;
        try
        {
            switch (frame.PixelFormat)
            {
                case VideoPixelFormat.Bgra8:
                    return CopyBgra(frame, destination, slot.Stride);
                case VideoPixelFormat.Nv12:
                    ImageConverter.Nv12ToBgra(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, frame.GetPlaneSpan(frame.Plane1), frame.Plane1.Stride, destination, slot.Stride, frame.Width, frame.Height, range);
                    return true;
                case VideoPixelFormat.Yuy2:
                    ImageConverter.Yuy2ToBgra(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, destination, slot.Stride, frame.Width, frame.Height, range);
                    return true;
                default:
                    return false;
            }
        }
        catch (ArgumentException)
        {
            // Unexpected buffer layout
            return false;
        }
    }

    private static bool CopyBgra(in FrameBuffer frame, Span<byte> destination, int stride)
    {
        var plane = frame.Plane0;
        var rowBytes = frame.Width * BytesPerPixel;
        var source = frame.GetPlaneSpan(plane);
        if ((plane.Stride < rowBytes) || (source.Length < (plane.Stride * (frame.Height - 1)) + rowBytes))
        {
            return false;
        }

        if (plane.Stride == stride)
        {
            source[..(stride * frame.Height)].CopyTo(destination);
        }
        else
        {
            for (var y = 0; y < frame.Height; y++)
            {
                source.Slice(y * plane.Stride, rowBytes).CopyTo(destination.Slice(y * stride, rowBytes));
            }
        }

        return true;
    }

    private void OnFailed(object? sender, VideoCaptureFailedEventArgs e)
    {
        log.ErrorCameraFailed(e.Message, e.Code);
        ErrorMessage = e.Message;
        _ = StopCaptureAsync();
    }
}
