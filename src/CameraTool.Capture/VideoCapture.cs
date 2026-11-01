namespace CameraTool.Capture;

using System.Globalization;

using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;

public sealed class VideoCapture : IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan StopWaitInterval = TimeSpan.FromMilliseconds(1);

    private readonly FrameCapturedEventArgs frameEventArgs = new();

    private MediaCapture? mediaCapture;

    private MediaFrameReader? frameReader;

    private int processing;

    private long sequence;

    public event EventHandler<FrameCapturedEventArgs>? FrameCaptured;

    public event EventHandler<VideoCaptureFailedEventArgs>? Failed;

    public string DeviceId { get; }

    public bool IsOpen => frameReader is not null;

    public bool IsCapturing { get; private set; }

    public VideoFormat Format { get; private set; }

    public VideoPixelFormat OutputPixelFormat { get; private set; }

    public VideoCapture(string deviceId)
    {
        DeviceId = deviceId;
    }

    public void Dispose() => CloseAsync().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);

    // outputPixelFormat: Unknown = native format, otherwise converted by MediaFrameReader
    public async Task OpenAsync(VideoFormat format, VideoPixelFormat outputPixelFormat = VideoPixelFormat.Unknown)
    {
        if (IsOpen)
        {
            throw new InvalidOperationException("Already opened.");
        }

        var group = await MediaFrameSourceGroup.FromIdAsync(DeviceId).AsTask().ConfigureAwait(false) ??
                    throw new InvalidOperationException("Device not found.");

        var capture = new MediaCapture();
        try
        {
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = group,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video
            }).AsTask().ConfigureAwait(false);

            var source = capture.FrameSources.Values.FirstOrDefault(static x => x.Info.SourceKind == MediaFrameSourceKind.Color) ??
                         throw new InvalidOperationException("Color source not found.");
            var mediaFormat = source.SupportedFormats.FirstOrDefault(x => FormatHelper.IsMatch(x, format)) ??
                              throw new InvalidOperationException("Format not supported.");
            await source.SetFormatAsync(mediaFormat).AsTask().ConfigureAwait(false);

            var reader = outputPixelFormat == VideoPixelFormat.Unknown
                ? await capture.CreateFrameReaderAsync(source).AsTask().ConfigureAwait(false)
                : await capture.CreateFrameReaderAsync(source, FormatHelper.ToSubtype(outputPixelFormat)).AsTask().ConfigureAwait(false);
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            reader.FrameArrived += OnFrameArrived;
            capture.Failed += OnFailed;

            Format = format;
            OutputPixelFormat = outputPixelFormat == VideoPixelFormat.Unknown ? format.PixelFormat : outputPixelFormat;
            mediaCapture = capture;
            frameReader = reader;
        }
        catch
        {
            capture.Dispose();
            throw;
        }
    }

    public async Task StartAsync()
    {
        var reader = frameReader ?? throw new InvalidOperationException("Not opened.");
        if (IsCapturing)
        {
            return;
        }

        var status = await reader.StartAsync().AsTask().ConfigureAwait(false);
        if (status != MediaFrameReaderStartStatus.Success)
        {
            throw new InvalidOperationException(String.Create(CultureInfo.InvariantCulture, $"Failed to start capture. status=[{status}]"));
        }

        IsCapturing = true;
    }

    public async Task StopAsync()
    {
        if ((frameReader is not { } reader) || !IsCapturing)
        {
            return;
        }

        await reader.StopAsync().AsTask().ConfigureAwait(false);
        IsCapturing = false;

        // Wait for the running handler
        while (Volatile.Read(ref processing) != 0)
        {
            await Task.Delay(StopWaitInterval).ConfigureAwait(false);
        }
    }

    public async Task CloseAsync()
    {
        await StopAsync().ConfigureAwait(false);

        if (frameReader is not null)
        {
            frameReader.FrameArrived -= OnFrameArrived;
            frameReader.Dispose();
            frameReader = null;
        }

        if (mediaCapture is not null)
        {
            mediaCapture.Failed -= OnFailed;
            mediaCapture.Dispose();
            mediaCapture = null;
        }
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        // Realtime mode: skip if the previous frame is still being processed
        if (Interlocked.Exchange(ref processing, 1) != 0)
        {
            return;
        }

        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            if (frame is null)
            {
                return;
            }

            var arrivalTime = CaptureClock.Now;
            using var bitmap = frame.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap is null)
            {
                return;
            }

            using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
            using var reference = buffer.CreateReference();
            if (!MemoryBufferAccessor.TryGetBuffer(reference, out var pointer, out var capacity))
            {
                return;
            }

            var plane0 = ToPlane(buffer.GetPlaneDescription(0));
            var plane1 = buffer.GetPlaneCount() > 1 ? ToPlane(buffer.GetPlaneDescription(1)) : default;
            frameEventArgs.Frame = new FrameBuffer(
                pointer,
                capacity,
                FormatHelper.FromBitmapPixelFormat(bitmap.BitmapPixelFormat),
                bitmap.PixelWidth,
                bitmap.PixelHeight,
                plane0,
                plane1,
                ++sequence,
                frame.SystemRelativeTime ?? arrivalTime,
                arrivalTime);
            FrameCaptured?.Invoke(this, frameEventArgs);
        }
        finally
        {
            frameEventArgs.Frame = default;
            Volatile.Write(ref processing, 0);
        }
    }

    private void OnFailed(MediaCapture sender, MediaCaptureFailedEventArgs args) =>
        Failed?.Invoke(this, new VideoCaptureFailedEventArgs(args.Message, args.Code));

    private static FramePlane ToPlane(BitmapPlaneDescription plane) =>
        new(plane.StartIndex, plane.Width, plane.Height, plane.Stride);
}
