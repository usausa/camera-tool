namespace CameraTool.WebStream.Services;

using System.Diagnostics;
using System.Numerics;

using CameraTool.Capture;
using CameraTool.Vision;
using CameraTool.WebStream.Settings;

// Encodes the frames to H.264 while WebSocket (H.264) clients are connected.
// The capture thread passes the frame to the encoder, and the output callback (encoder thread) publishes the access unit
// with the latest detection results (faces, motion and objects).
// The encoder is created in the background when a client connects (again when the frame format changes),
// and released when no client has been connected for the idle time.
// An encoder that fails (to be created, or later) is released and tried again after the retry interval.
public sealed class H264StreamService : IHostedService, IDisposable
{
    private const int MinimumCapacity = 16 * 1024;

    // Frames passed to the encoder and not output yet (the encoder holds a few frames at most)
    private const int PendingCount = 8;

    private const int DefaultFrameRate = 30;

    private const int BitsPerKilobit = 1000;

    private static readonly TimeSpan IdleTime = TimeSpan.FromSeconds(10);

    // Also checked while no frame arrives (the camera stopped)
    private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromSeconds(1);

    // The device may come back or free its sessions
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(10);

    // A slow client that skips frames must not make every frame a key frame
    private static readonly TimeSpan KeyFrameRequestInterval = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<H264StreamService> log;

    private readonly H264Settings settings;

    private readonly VideoSource source;

    private readonly FrameHub hub;

    // Encoder thread only (one encoder runs at a time)
    private readonly FrameHeaderWriter headerWriter;

    private readonly FramePool pool = new();

    // Guards the encoder against the release while a frame is passed
    private readonly Lock encoderSync = new();

    private readonly Lock pendingSync = new();

    private readonly PendingFrame[] pendingFrames = new PendingFrame[PendingCount];

    private readonly LatencyCounter inputTime = new();

    private readonly LatencyCounter encodeTime = new();

    private readonly LatencyCounter streamLatency = new();

    private H264Encoder? encoder;

    // Format of the encoder (also while it is created)
    private EncoderFormat encoderFormat;

    // Creation or release of the encoder
    private Task? replaceTask;

    // Format the encoder failed with, tried again after the retry interval
    private EncoderFormat failedFormat;

    // Stopwatch timestamp
    private long failedTimestamp;

    private bool stopped;

    private long releasedDroppedFrames;

    private Timer? idleTimer;

    // Stopwatch timestamp
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
        headerWriter = new FrameHeaderWriter(detection, motion, objectDetection);
    }

    public void Dispose()
    {
        // Released by StopAsync unless the host did not stop
        idleTimer?.Dispose();
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
            idleTimer = new Timer(OnIdleTimer, null, IdleCheckInterval, IdleCheckInterval);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        source.FrameCaptured -= OnFrameCaptured;
        if (idleTimer is not null)
        {
            await idleTimer.DisposeAsync();
        }

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
            Bitrate = settings.ResolveBitrate(width, height) * BitsPerKilobit,
            KeyFrameInterval = settings.KeyFrameInterval * frameRate,
            PreferHardware = settings.PreferHardware,
            FullRange = format.FullRange
        };
        var start = Stopwatch.GetTimestamp();
        H264Encoder? created = null;
        try
        {
            created = new H264Encoder(options, (data, keyFrame, timestamp) => OnEncoded(data, keyFrame, timestamp, format));
            log.InfoH264EncoderCreated(created.Name, created.IsHardware, width, height, format.FullRange, frameRate, options.Bitrate / BitsPerKilobit, options.KeyFrameInterval, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            lock (encoderSync)
            {
                if (!stopped)
                {
                    encoder = created;
                    EncoderName = created.Name;
                    IsHardware = created.IsHardware;
                    ErrorMessage = null;
                    failedFormat = default;
                    created = null;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            log.ErrorH264EncoderCreateFailed(ex, width, height);
            lock (encoderSync)
            {
                SetFailed(format, ex.Message);
            }
        }
        finally
        {
            // Stopped while creating
            created?.Dispose();
        }
    }

    // In the lock: no encoder for the format until the retry interval has passed
    private void SetFailed(EncoderFormat format, string message)
    {
        failedFormat = format;
        failedTimestamp = Stopwatch.GetTimestamp();
        ErrorMessage = message;
    }

    // Timer thread: releases the encoder when no client has been connected for the idle time
    private void OnIdleTimer(object? state)
    {
        var now = Stopwatch.GetTimestamp();
        if (hub.HasClients)
        {
            Interlocked.Exchange(ref lastClientTimestamp, now);
            return;
        }

        lock (encoderSync)
        {
            if (!stopped && (encoder is not null) && replaceTask is not { IsCompleted: false } &&
                (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastClientTimestamp), now) > IdleTime))
            {
                Replace(default);
            }
        }
    }

    //--------------------------------------------------------------------------------
    // Capture thread
    //--------------------------------------------------------------------------------

    // The idle release is done by the timer
    private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
    {
        if (!hub.HasClients)
        {
            return;
        }

        var frame = e.Frame;
        Interlocked.Exchange(ref lastClientTimestamp, Stopwatch.GetTimestamp());
        lock (encoderSync)
        {
            // Not encoded while the encoder is created or released
            if (stopped || replaceTask is { IsCompleted: false })
            {
                return;
            }

            // YUV without range information is treated as video range
            var format = new EncoderFormat(frame.Width, frame.Height, frame.ColorRange == VideoColorRange.Full);
            if ((encoder is null) || (encoderFormat != format))
            {
                if ((encoder is not null) || (failedFormat != format) || (Stopwatch.GetElapsedTime(failedTimestamp) > RetryInterval))
                {
                    Replace(format);
                }

                return;
            }

            Encode(encoder, frame);
        }
    }

    // In the lock. The encoder takes the range of the frame (written to the stream).
    private void Encode(H264Encoder current, in FrameBuffer frame)
    {
        var start = Stopwatch.GetTimestamp();
        var format = frame.PixelFormat;
        if ((format != VideoPixelFormat.Nv12) && (format != VideoPixelFormat.Yuy2))
        {
            return;
        }

        try
        {
            // The capture time (100 ns units) is returned with the output and identifies the frame
            var timestamp = frame.CaptureTime.Ticks;
            lock (pendingSync)
            {
                pendingFrames[pendingIndex] = new PendingFrame(timestamp, frame.Sequence, frame.ArrivalTime, start);
                pendingIndex = (pendingIndex + 1) % PendingCount;
            }

            if (format == VideoPixelFormat.Yuy2)
            {
                // Converted into the input sample of the encoder
                current.EncodeYuy2(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, timestamp);
            }
            else
            {
                current.Encode(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, frame.GetPlaneSpan(frame.Plane1), frame.Plane1.Stride, timestamp);
            }
        }
        catch (ArgumentException ex)
        {
            // Unexpected buffer layout
            log.ErrorEncodeFailed(ex);
            return;
        }

        if (current.IsFaulted)
        {
            // Released, and created again after the retry interval
            log.ErrorH264EncoderFailed(current.Name, current.FaultCode);
            SetFailed(encoderFormat, String.Create(CultureInfo.InvariantCulture, $"H.264 encoder failed. code=[0x{current.FaultCode:X8}]"));
            Replace(default);
            return;
        }

        inputTime.Add(Stopwatch.GetElapsedTime(start));
    }

    //--------------------------------------------------------------------------------
    // Encoder thread
    //--------------------------------------------------------------------------------

    private void OnEncoded(ReadOnlySpan<byte> data, bool keyFrame, long timestamp, EncoderFormat format)
    {
        var found = TryGetPending(timestamp, out var pending);
        var encoded = pool.Rent();
        try
        {
            // The pooled buffers grow to the largest size (key frames)
            data.CopyTo(encoded.GetDataBuffer((int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(data.Length, MinimumCapacity))));

            var info = new FrameHeaderInfo(format.Width, format.Height, pending.Sequence, TimeSpan.FromTicks(timestamp), keyFrame, format.FullRange);
            var now = headerWriter.Write(encoded, Interlocked.Increment(ref encodedFrames), info, data.Length);
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
