namespace CameraTool.WebStream.Components;

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

using CameraTool.Capture;
using CameraTool.Vision;
using CameraTool.WebStream.Settings;

// Encodes the frames to H.264 while WebSocket (H.264) clients are connected.
// The capture thread passes the frame to the encoder, and the output callback (encoder thread) publishes the access unit
// with the latest detection results (faces, motion and objects).
// The encoder is created in the background when a client connects (again when the frame size changes),
// and released when no client has been connected for the idle time.
public sealed class H264StreamService : IHostedService, IDisposable
{
    private const int MinimumCapacity = 16 * 1024;

    // Frames passed to the encoder and not output yet (the encoder holds a few frames at most)
    private const int PendingCount = 8;

    private const int DefaultFrameRate = 30;

    private const int BitsPerKilobit = 1000;

    private static readonly TimeSpan IdleTime = TimeSpan.FromSeconds(10);

    // A slow client that skips frames must not make every frame a key frame
    private static readonly TimeSpan KeyFrameRequestInterval = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<H264StreamService> log;

    private readonly H264Settings settings;

    private readonly VideoSource source;

    private readonly FrameHub hub;

    private readonly FaceDetectionService detection;

    private readonly MotionService motion;

    private readonly ObjectDetectionService objectDetection;

    // Range of the frame (written to the stream), capture thread only
    private readonly Nv12Planes planes = new(false);

    private readonly FramePool pool = new();

    // Guards the encoder against the release while a frame is passed
    private readonly Lock encoderSync = new();

    private readonly Lock pendingSync = new();

    private readonly PendingFrame[] pendingFrames = new PendingFrame[PendingCount];

    // Detection results attached to the frame (encoder thread only)
    private readonly FaceBox[] faces = new FaceBox[FaceDetectionService.MaxFaces];

    private readonly byte[] motionMask = new byte[MotionDetector.MaskLength];

    private readonly ObjectBox[] objects = new ObjectBox[ObjectDetectionService.MaxObjects];

    private readonly LatencyCounter inputTime = new();

    private readonly LatencyCounter encodeTime = new();

    private readonly LatencyCounter streamLatency = new();

    private H264Encoder? encoder;

    // Format of the encoder (also while it is created)
    private EncoderFormat encoderFormat;

    // Creation or release of the encoder
    private Task? replaceTask;

    // Format the encoder could not be created for (not retried)
    private EncoderFormat failedFormat;

    private bool stopped;

    private long releasedDroppedFrames;

    // Stopwatch timestamp (capture thread only)
    private long lastClientTimestamp;

    private int pendingIndex;

    // Stopwatch timestamp
    private long lastKeyFrameRequest;

    private long encodedFrames;

    private long encodedBytes;

    private long keyFrames;

    public bool IsEnabled => settings.Enable;

    // Encoder used last
    public string? EncoderName { get; private set; }

    public bool IsHardware { get; private set; }

    public string? ErrorMessage { get; private set; }

    public long EncodedFrames => Interlocked.Read(ref encodedFrames);

    public long EncodedBytes => Interlocked.Read(ref encodedBytes);

    public long KeyFrames => Interlocked.Read(ref keyFrames);

    // Frames not encoded because the encoder was busy
    public long DroppedFrames
    {
        get
        {
            lock (encoderSync)
            {
                return releasedDroppedFrames + (encoder?.DroppedFrames ?? 0);
            }
        }
    }

    public int AllocatedFrames => pool.Created;

    public H264StreamService(ILogger<H264StreamService> log, H264Settings settings, VideoSource source, StreamHubs hubs, FaceDetectionService detection, MotionService motion, ObjectDetectionService objectDetection)
    {
        this.log = log;
        this.settings = settings;
        this.source = source;
        hub = hubs.H264;
        this.detection = detection;
        this.motion = motion;
        this.objectDetection = objectDetection;
    }

    public void Dispose()
    {
        // Released by StopAsync unless the host did not stop
        TakeEncoder(out _)?.Dispose();
    }

    //--------------------------------------------------------------------------------
    // Hosted service
    //--------------------------------------------------------------------------------

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (settings.Enable)
        {
            source.FrameCaptured += OnFrameCaptured;
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        source.FrameCaptured -= OnFrameCaptured;

        var current = TakeEncoder(out var task);
        if (task is not null)
        {
            await task;
        }

        current?.Dispose();
    }

    // Stops the encoding, the running creation releases its encoder
    private H264Encoder? TakeEncoder(out Task? task)
    {
        lock (encoderSync)
        {
            stopped = true;
            var current = encoder;
            encoder = null;
            task = replaceTask;
            return current;
        }
    }

    //--------------------------------------------------------------------------------
    // Key frame
    //--------------------------------------------------------------------------------

    // Requested by a client that waits for a key frame (a new client or a client that skipped a frame),
    // a request within the interval from the previous one is ignored (the client requests again with the next frame)
    public void RequestKeyFrame()
    {
        var last = Interlocked.Read(ref lastKeyFrameRequest);
        var now = Stopwatch.GetTimestamp();
        if ((last != 0) && (Stopwatch.GetElapsedTime(last, now) < KeyFrameRequestInterval))
        {
            return;
        }

        if (Interlocked.CompareExchange(ref lastKeyFrameRequest, now, last) == last)
        {
            // A new encoder starts with a key frame
            Volatile.Read(ref encoder)?.RequestKeyFrame();
        }
    }

    //--------------------------------------------------------------------------------
    // Statistics
    //--------------------------------------------------------------------------------

    // Frame -> encoder input (conversion and copy)
    public LatencySnapshot TakeInputTime() => inputTime.Take();

    // Encoder input -> output
    public LatencySnapshot TakeEncodeTime() => encodeTime.Take();

    // Arrival -> published
    public LatencySnapshot TakeStreamLatency() => streamLatency.Take();

    //--------------------------------------------------------------------------------
    // Encoder
    //--------------------------------------------------------------------------------

    // In the lock: releases the encoder and creates one for the format (default: none) in the background
    private void Replace(EncoderFormat format)
    {
        var previous = encoder;
        if (previous is not null)
        {
            releasedDroppedFrames += previous.DroppedFrames;
        }

        encoder = null;
        encoderFormat = format;
        replaceTask = Task.Run(() => ReplaceEncoder(previous, format));
    }

    private void ReplaceEncoder(H264Encoder? previous, EncoderFormat format)
    {
        if (previous is not null)
        {
            // Waits for the remaining output
            previous.Dispose();
            log.InfoH264EncoderReleased(previous.Name);
        }

        var width = format.Width;
        var height = format.Height;
        if (width == 0)
        {
            return;
        }

        var frameRate = Math.Max(1, (int)Math.Round(source.Format?.FrameRate ?? DefaultFrameRate));
        var options = new H264EncoderOptions
        {
            Width = width,
            Height = height,
            FrameRate = frameRate,
            Bitrate = GetBitrate(width, height) * BitsPerKilobit,
            KeyFrameInterval = settings.KeyFrameInterval * frameRate,
            PreferHardware = settings.PreferHardware,
            FullRange = format.FullRange
        };
        var start = Stopwatch.GetTimestamp();
        H264Encoder? created = null;
        try
        {
            created = new H264Encoder(options, (data, keyFrame, timestamp) => OnEncoded(data, keyFrame, timestamp, width, height));
            log.InfoH264EncoderCreated(created.Name, created.IsHardware, width, height, format.FullRange, frameRate, options.Bitrate / BitsPerKilobit, options.KeyFrameInterval, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            lock (encoderSync)
            {
                if (!stopped)
                {
                    encoder = created;
                    EncoderName = created.Name;
                    IsHardware = created.IsHardware;
                    ErrorMessage = null;
                    created = null;
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            log.ErrorH264EncoderCreateFailed(ex, width, height);
            lock (encoderSync)
            {
                failedFormat = format;
                ErrorMessage = ex.Message;
            }
        }
        finally
        {
            // Stopped while creating
            created?.Dispose();
        }
    }

    // kbps, about the same quality as the JPEG of quality 80 when it is not set
    private int GetBitrate(int width, int height)
    {
        if (settings.Bitrate > 0)
        {
            return settings.Bitrate;
        }

        var pixels = width * height;
        if (pixels <= 640 * 480)
        {
            return 4000;
        }

        return pixels <= 1280 * 720 ? 8000 : 16000;
    }

    //--------------------------------------------------------------------------------
    // Capture thread
    //--------------------------------------------------------------------------------

    private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
    {
        var frame = e.Frame;
        var active = hub.HasClients;
        if (active)
        {
            lastClientTimestamp = Stopwatch.GetTimestamp();
        }

        lock (encoderSync)
        {
            // Not encoded while the encoder is created or released
            if (stopped || replaceTask is { IsCompleted: false })
            {
                return;
            }

            if (!active)
            {
                if ((encoder is not null) && (Stopwatch.GetElapsedTime(lastClientTimestamp) > IdleTime))
                {
                    Replace(default);
                }

                return;
            }

            // YUV without range information is treated as video range
            var format = new EncoderFormat(frame.Width, frame.Height, frame.ColorRange == VideoColorRange.Full);
            if ((encoder is null) || (encoderFormat != format))
            {
                if ((encoder is not null) || (failedFormat != format))
                {
                    Replace(format);
                }

                return;
            }

            Encode(encoder, frame);
        }
    }

    private void Encode(H264Encoder current, in FrameBuffer frame)
    {
        var start = Stopwatch.GetTimestamp();
        if (!planes.TryGet(frame, out var y, out var yStride, out var uv, out var uvStride))
        {
            return;
        }

        // The capture time (100 ns units) is returned with the output and identifies the frame
        var timestamp = frame.CaptureTime.Ticks;
        lock (pendingSync)
        {
            pendingFrames[pendingIndex] = new PendingFrame(timestamp, frame.Sequence, frame.ArrivalTime, start);
            pendingIndex = (pendingIndex + 1) % PendingCount;
        }

        try
        {
            current.Encode(y, yStride, uv, uvStride, timestamp);
        }
        catch (ArgumentException ex)
        {
            // Unexpected buffer layout
            log.ErrorEncodeFailed(ex);
            return;
        }

        inputTime.Add(Stopwatch.GetElapsedTime(start));
    }

    //--------------------------------------------------------------------------------
    // Encoder thread
    //--------------------------------------------------------------------------------

    private void OnEncoded(ReadOnlySpan<byte> data, bool keyFrame, long timestamp, int width, int height)
    {
        var found = TryGetPending(timestamp, out var pending);
        var encoded = pool.Rent();
        try
        {
            // The pooled buffers grow to the largest size (key frames)
            data.CopyTo(encoded.GetDataBuffer((int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(data.Length, MinimumCapacity))));

            // Latest detection results
            FaceDetectionInfo? detected = detection.IsEnabled && detection.IsAvailable ? detection.CopyResults(faces) : null;
            MotionInfo? moved = motion.IsEnabled ? motion.CopyResults(motionMask) : null;
            ObjectDetectionInfo? detectedObjects = objectDetection.IsEnabled && objectDetection.IsAvailable ? objectDetection.CopyResults(objects) : null;

            // Unix time of the capture, calculated from the capture clock
            var now = CaptureClock.Now;
            var unixNow = (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
            var header = new FrameHeader
            {
                Width = width,
                Height = height,
                Sequence = pending.Sequence,
                CaptureTime = unixNow - (now - TimeSpan.FromTicks(timestamp)).TotalMilliseconds,
                EncodedTime = unixNow,
                KeyFrame = keyFrame,
                Detection = detected,
                Faces = faces,
                Motion = moved,
                MotionMask = motionMask,
                ObjectDetection = detectedObjects,
                Objects = objects
            };
            var headerLength = StreamProtocol.GetHeaderLength(header);
            StreamProtocol.WriteHeader(encoded.GetHeaderBuffer(headerLength), header);
            encoded.Commit(Interlocked.Increment(ref encodedFrames), headerLength, data.Length, keyFrame);
            hub.Publish(encoded);
            encoded = null;

            if (found)
            {
                encodeTime.Add(Stopwatch.GetElapsedTime(pending.InputTimestamp));
                streamLatency.Add(now - pending.ArrivalTime);
            }
        }
        finally
        {
            encoded?.Release();
        }

        Interlocked.Add(ref encodedBytes, data.Length);
        if (keyFrame)
        {
            Interlocked.Increment(ref keyFrames);
        }
    }

    private bool TryGetPending(long timestamp, out PendingFrame frame)
    {
        lock (pendingSync)
        {
            foreach (var pending in pendingFrames)
            {
                if (pending.Timestamp == timestamp)
                {
                    frame = pending;
                    return true;
                }
            }
        }

        frame = default;
        return false;
    }

    private readonly record struct EncoderFormat(int Width, int Height, bool FullRange);

    // InputTimestamp: Stopwatch timestamp
    private readonly record struct PendingFrame(long Timestamp, long Sequence, TimeSpan ArrivalTime, long InputTimestamp);
}
