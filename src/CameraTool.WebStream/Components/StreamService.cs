namespace CameraTool.WebStream.Components;

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

    private readonly FaceDetectionService detection;

    private readonly MotionService motion;

    private readonly ObjectDetectionService objectDetection;

    private readonly JpegEncoder encoder;

    // Detection results attached to the frame (capture thread only)
    private readonly FaceBox[] faces = new FaceBox[FaceDetectionService.MaxFaces];

    private readonly byte[] motionMask = new byte[MotionDetector.MaskLength];

    private readonly ObjectBox[] objects = new ObjectBox[ObjectDetectionService.MaxObjects];

    // Full range (JFIF), capture thread only
    private readonly Nv12Planes planes = new(true);

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
        this.detection = detection;
        this.motion = motion;
        this.objectDetection = objectDetection;
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

        // Latest detection results, detected in a previous frame
        FaceDetectionInfo? detected = detection.IsEnabled && detection.IsAvailable ? detection.CopyResults(faces) : null;
        MotionInfo? moved = motion.IsEnabled ? motion.CopyResults(motionMask) : null;
        ObjectDetectionInfo? found = objectDetection.IsEnabled && objectDetection.IsAvailable ? objectDetection.CopyResults(objects) : null;

        // Unix time of the capture, calculated from the capture clock
        var now = CaptureClock.Now;
        var unixNow = (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
        var header = new FrameHeader
        {
            Width = width,
            Height = height,
            Sequence = frame.Sequence,
            CaptureTime = unixNow - (now - frame.CaptureTime).TotalMilliseconds,
            EncodedTime = unixNow,
            Detection = detected,
            Faces = faces,
            Motion = moved,
            MotionMask = motionMask,
            ObjectDetection = found,
            Objects = objects
        };
        var headerLength = StreamProtocol.GetHeaderLength(header);
        StreamProtocol.WriteHeader(encoded.GetHeaderBuffer(headerLength), header);
        encoded.Commit(Interlocked.Increment(ref encodedFrames), headerLength, written, false);

        Interlocked.Add(ref encodedBytes, written);
        convertTime.Add(Stopwatch.GetElapsedTime(start, converted));
        encodeTime.Add(Stopwatch.GetElapsedTime(converted));
        streamLatency.Add(now - frame.ArrivalTime);
        return true;
    }
}
