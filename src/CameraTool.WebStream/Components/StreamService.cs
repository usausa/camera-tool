namespace CameraTool.WebStream.Components;

using System.Diagnostics;
using System.Runtime.InteropServices;

using CameraTool.Capture;
using CameraTool.Vision;
using CameraTool.WebStream.Settings;

// Encodes the frame on the capture thread and publishes it with the latest detection results
public sealed class StreamService : IHostedService, IDisposable
{
    private const int MinimumCapacity = 16 * 1024;

    private readonly ILogger<StreamService> log;

    private readonly VideoSource source;

    private readonly FrameHub hub;

    private readonly FaceDetectionService detection;

    private readonly JpegEncoder encoder;

    // Detection results attached to the frame (capture thread only)
    private readonly FaceBox[] faces = new FaceBox[FaceDetectionService.MaxFaces];

    private readonly FramePool pool = new();

    private readonly LatencyCounter convertTime = new();

    private readonly LatencyCounter encodeTime = new();

    private readonly LatencyCounter streamLatency = new();

    // Full range NV12 converted from the frame (capture thread only)
    private byte[] planes = [];

    private long encodedFrames;

    private long encodedBytes;

    public long EncodedFrames => Interlocked.Read(ref encodedFrames);

    public long EncodedBytes => Interlocked.Read(ref encodedBytes);

    public int AllocatedFrames => pool.Created;

    public StreamService(ILogger<StreamService> log, StreamSettings settings, VideoSource source, FrameHub hub, FaceDetectionService detection)
    {
        this.log = log;
        this.source = source;
        this.hub = hub;
        this.detection = detection;
        encoder = new JpegEncoder(settings.Quality);
    }

    public void Dispose()
    {
        encoder.Dispose();
    }

    //--------------------------------------------------------------------------------
    // Hosted service
    //--------------------------------------------------------------------------------

    public Task StartAsync(CancellationToken cancellationToken)
    {
        source.FrameCaptured += OnFrameCaptured;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        source.FrameCaptured -= OnFrameCaptured;
        return Task.CompletedTask;
    }

    //--------------------------------------------------------------------------------
    // Statistics
    //--------------------------------------------------------------------------------

    public LatencySnapshot TakeConvertTime() => convertTime.Take();

    public LatencySnapshot TakeEncodeTime() => encodeTime.Take();

    public LatencySnapshot TakeStreamLatency() => streamLatency.Take();

    //--------------------------------------------------------------------------------
    // Encode
    //--------------------------------------------------------------------------------

    // Capture thread
    private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
    {
        if (!hub.HasClients)
        {
            return;
        }

        var encoded = pool.Rent();
        try
        {
            if (Encode(e.Frame, encoded))
            {
                hub.Publish(encoded);
                encoded = null;
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            log.ErrorEncodeFailed(ex);
        }
        finally
        {
            encoded?.Release();
        }
    }

    private bool Encode(in FrameBuffer frame, EncodedFrame encoded)
    {
        var start = Stopwatch.GetTimestamp();
        if (!TryGetPlanes(frame, out var y, out var yStride, out var uv, out var uvStride))
        {
            return false;
        }

        var converted = Stopwatch.GetTimestamp();
        var width = frame.Width;
        var height = frame.Height;
        var capacity = Math.Max((width * height) / 4, MinimumCapacity);
        int written;
        while (!encoder.TryEncodeNv12(y, yStride, uv, uvStride, width, height, encoded.GetJpegBuffer(capacity), out written))
        {
            capacity = Math.Min(capacity * 2, JpegEncoder.GetMaxLength(width, height));
            log.DebugEncodeBufferExpanded(capacity);
        }

        // Latest detection results, detected in a previous frame
        FaceDetectionInfo? detected = detection.IsEnabled && detection.IsAvailable ? detection.CopyResults(faces) : null;

        // Unix time of the capture, calculated from the capture clock
        var now = CaptureClock.Now;
        var unixNow = (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
        var captureTime = unixNow - (now - frame.CaptureTime).TotalMilliseconds;
        var headerLength = StreamProtocol.GetHeaderLength(detected);
        StreamProtocol.WriteHeader(encoded.GetHeaderBuffer(headerLength), width, height, frame.Sequence, captureTime, unixNow, detected, faces);
        encoded.Commit(Interlocked.Increment(ref encodedFrames), headerLength, written);

        Interlocked.Add(ref encodedBytes, written);
        convertTime.Add(Stopwatch.GetElapsedTime(start, converted));
        encodeTime.Add(Stopwatch.GetElapsedTime(converted));
        streamLatency.Add(now - frame.ArrivalTime);
        return true;
    }

    // Full range NV12 planes for the encoder
    private bool TryGetPlanes(in FrameBuffer frame, out ReadOnlySpan<byte> y, out int yStride, out ReadOnlySpan<byte> uv, out int uvStride)
    {
        var width = frame.Width;
        var height = frame.Height;
        switch (frame.PixelFormat)
        {
            case VideoPixelFormat.Nv12 when frame.ColorRange == VideoColorRange.Full:
                // Passed without copy
                y = frame.GetPlaneSpan(frame.Plane0);
                yStride = frame.Plane0.Stride;
                uv = frame.GetPlaneSpan(frame.Plane1);
                uvStride = frame.Plane1.Stride;
                return true;
            case VideoPixelFormat.Nv12:
            {
                // YUV without range information is treated as video range
                var stride = PreparePlanes(width, height);
                var destinationY = planes.AsSpan(0, stride * height);
                var destinationUv = planes.AsSpan(stride * height);
                ImageConverter.Nv12ToFullRange(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, frame.GetPlaneSpan(frame.Plane1), frame.Plane1.Stride, destinationY, stride, destinationUv, stride, width, height);
                y = destinationY;
                uv = destinationUv;
                yStride = stride;
                uvStride = stride;
                return true;
            }

            case VideoPixelFormat.Yuy2:
            {
                var stride = PreparePlanes(width, height);
                var destinationY = planes.AsSpan(0, stride * height);
                var destinationUv = planes.AsSpan(stride * height);
                var range = frame.ColorRange == VideoColorRange.Full ? YuvRange.Full : YuvRange.Limited;
                ImageConverter.Yuy2ToNv12(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, destinationY, stride, destinationUv, stride, width, height, range);
                y = destinationY;
                uv = destinationUv;
                yStride = stride;
                uvStride = stride;
                return true;
            }

            default:
                y = default;
                uv = default;
                yStride = 0;
                uvStride = 0;
                return false;
        }
    }

    // Returns the stride of both planes
    private int PreparePlanes(int width, int height)
    {
        var stride = ((width + 1) / 2) * 2;
        var size = (stride * height) + (stride * ((height + 1) / 2));
        if (planes.Length != size)
        {
            planes = GC.AllocateUninitializedArray<byte>(size);
        }

        return stride;
    }
}
