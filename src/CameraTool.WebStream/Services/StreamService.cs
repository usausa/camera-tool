namespace CameraTool.WebStream.Services;

using System.Diagnostics;
using System.Runtime.InteropServices;

using CameraTool.Capture;
using CameraTool.Vision;
using CameraTool.WebStream.Settings;

// Encodes the frame to JPEG on the capture thread and publishes it with the latest detection results (faces, motion and objects)
public sealed class StreamService : IHostedService, IDisposable
{
    private const int MinimumCapacity = 16 * 1024;

    private readonly ILogger<StreamService> log;

    private readonly VideoSource source;

    private readonly FrameHub hub;

    private readonly JpegEncoder encoder;

    // Capture thread only
    private readonly FrameHeaderWriter headerWriter;

    // Full range (JFIF), capture thread only
    private readonly Nv12Planes planes = new();

    private readonly FramePool pool = new();

    private readonly LatencyCounter convertTime = new();

    private readonly LatencyCounter encodeTime = new();

    private readonly LatencyCounter streamLatency = new();

    private long encodedFrames;

    private long encodedBytes;

    public long EncodedFrames => Interlocked.Read(ref encodedFrames);

    public long EncodedBytes => Interlocked.Read(ref encodedBytes);

    public int AllocatedFrames => pool.Created;

    public StreamService(ILogger<StreamService> log, StreamSettings settings, VideoSource source, StreamHubs hubs, FaceDetectionService detection, MotionService motion, ObjectDetectionService objectDetection)
    {
        this.log = log;
        this.source = source;
        hub = hubs.Jpeg;
        encoder = new JpegEncoder(settings.Quality);
        headerWriter = new FrameHeaderWriter(detection, motion, objectDetection);
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
        if (!planes.TryGet(frame, out var y, out var yStride, out var uv, out var uvStride))
        {
            return false;
        }

        var converted = Stopwatch.GetTimestamp();
        var width = frame.Width;
        var height = frame.Height;
        var capacity = Math.Max((width * height) / 4, MinimumCapacity);
        int written;
        while (!encoder.TryEncodeNv12(y, yStride, uv, uvStride, width, height, encoded.GetDataBuffer(capacity), out written))
        {
            capacity = Math.Min(capacity * 2, JpegEncoder.GetMaxLength(width, height));
            log.DebugEncodeBufferExpanded(capacity);
        }

        var now = headerWriter.Write(encoded, Interlocked.Increment(ref encodedFrames), new FrameHeaderInfo(width, height, frame.Sequence, frame.CaptureTime, false, false), written);

        Interlocked.Add(ref encodedBytes, written);
        convertTime.Add(Stopwatch.GetElapsedTime(start, converted));
        encodeTime.Add(Stopwatch.GetElapsedTime(converted));
        streamLatency.Add(now - frame.ArrivalTime);
        return true;
    }
}
