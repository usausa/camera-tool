namespace CameraTool.WebStream.Components;

using System.Diagnostics;

using CameraTool.Capture;
using CameraTool.Vision;
using CameraTool.WebStream.Settings;

public readonly record struct MotionInfo(long Sequence, bool Detected, float Level, int Cells);

// Detects motion in the detection area on the capture thread while clients are connected.
// Handles the frame after the stream (the header has the motion of the previous frame) and before the face detection.
public sealed class MotionService : IHostedService
{
    private readonly ILogger<MotionService> log;

    private readonly VideoSource source;

    private readonly StreamHubs hubs;

    private readonly AreaService area;

    private readonly MotionDetector detector;

    private readonly Lock resultSync = new();

    private readonly byte[] workMask = new byte[MotionDetector.MaskLength];

    private readonly byte[] mask = new byte[MotionDetector.MaskLength];

    private readonly LatencyCounter processTime = new();

    private volatile bool enabled;

    private volatile bool resetRequested = true;

    private MotionInfo info;

    // Stopwatch timestamp
    private long lastMotionTimestamp;

    public bool IsEnabled
    {
        get => enabled;
        set
        {
            if (value)
            {
                // The background is created again from the next frame
                resetRequested = true;
            }

            enabled = value;
            if (!value)
            {
                Clear();
            }

            log.InfoMotionDetectionChanged(value);
        }
    }

    public MotionInfo Info
    {
        get
        {
            lock (resultSync)
            {
                return info;
            }
        }
    }

    public MotionService(ILogger<MotionService> log, MotionSettings settings, VideoSource source, StreamHubs hubs, AreaService area)
    {
        this.log = log;
        this.source = source;
        this.hubs = hubs;
        this.area = area;
        detector = new MotionDetector(new MotionDetectorOptions
        {
            Threshold = settings.Threshold,
            CellRatio = settings.CellRatio,
            MinArea = settings.MinArea,
            BackgroundRate = settings.BackgroundRate
        });
        enabled = settings.Enable;
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
    // Result
    //--------------------------------------------------------------------------------

    public MotionInfo CopyResults(Span<byte> destination)
    {
        lock (resultSync)
        {
            mask.CopyTo(destination);
            return info;
        }
    }

    // True while the last motion is within the hold time
    public bool IsMoving(TimeSpan holdTime) => Stopwatch.GetElapsedTime(Interlocked.Read(ref lastMotionTimestamp)) <= holdTime;

    public LatencySnapshot TakeProcessTime() => processTime.Take();

    private void Clear()
    {
        lock (resultSync)
        {
            Array.Clear(mask);
            info = default;
        }
    }

    //--------------------------------------------------------------------------------
    // Capture thread
    //--------------------------------------------------------------------------------

    private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
    {
        if (!enabled)
        {
            return;
        }

        // The results are used only by the stream, the background is created again when a client connects
        if (!hubs.HasClients)
        {
            if (!resetRequested)
            {
                resetRequested = true;
                Clear();
            }

            return;
        }

        var start = Stopwatch.GetTimestamp();
        if (resetRequested)
        {
            // Treated as moving until the new background is known
            resetRequested = false;
            detector.Reset();
            Interlocked.Exchange(ref lastMotionTimestamp, start);
        }

        var frame = e.Frame;
        detector.Area = area.Current;
        if (!Detect(frame, out var result))
        {
            return;
        }

        processTime.Add(Stopwatch.GetElapsedTime(start));
        if (result.Detected)
        {
            Interlocked.Exchange(ref lastMotionTimestamp, start);
        }

        lock (resultSync)
        {
            // Not published after disabled
            if (enabled)
            {
                workMask.CopyTo(mask, 0);
                info = new MotionInfo(frame.Sequence, result.Detected, result.Level, result.Cells);
            }
        }
    }

    private bool Detect(in FrameBuffer frame, out MotionResult result)
    {
        try
        {
            switch (frame.PixelFormat)
            {
                case VideoPixelFormat.Bgra8:
                    result = detector.DetectBgra(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, frame.Width, frame.Height, workMask);
                    return true;
                case VideoPixelFormat.Nv12:
                    result = detector.DetectNv12(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, frame.Width, frame.Height, workMask);
                    return true;
                case VideoPixelFormat.Yuy2:
                    result = detector.DetectYuy2(frame.GetPlaneSpan(frame.Plane0), frame.Plane0.Stride, frame.Width, frame.Height, workMask);
                    return true;
                default:
                    result = default;
                    return false;
            }
        }
        catch (ArgumentException)
        {
            // Unexpected buffer layout
            result = default;
            return false;
        }
    }
}
