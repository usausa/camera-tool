namespace CameraTool.FaceViewer.Components;

using System.Diagnostics;

using CameraTool.Capture;
using CameraTool.FaceViewer.Settings;
using CameraTool.Vision;

public readonly record struct MotionInfo(long Sequence, int FrameWidth, int FrameHeight, bool Detected, float Level, int Cells);

// Detects motion in the detection area on the capture thread.
// Subscribes to the frames before FaceDetectionService, so the motion of the frame is known when it decides to detect.
public sealed class MotionService : IDisposable
{
    private readonly VideoSource videoSource;

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

    // Raised on the capture thread when the cells change
    public event EventHandler? Updated;

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

    public MotionService(MotionSettings settings, VideoSource videoSource, AreaService area)
    {
        this.videoSource = videoSource;
        this.area = area;
        detector = new MotionDetector(new MotionDetectorOptions
        {
            Threshold = settings.Threshold,
            CellRatio = settings.CellRatio,
            MinArea = settings.MinArea,
            BackgroundRate = settings.BackgroundRate
        });
        enabled = settings.Enable;

        videoSource.FrameCaptured += OnFrameCaptured;
        videoSource.StateChanged += OnStateChanged;
    }

    public void Dispose()
    {
        videoSource.FrameCaptured -= OnFrameCaptured;
        videoSource.StateChanged -= OnStateChanged;
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

        Updated?.Invoke(this, EventArgs.Empty);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (!videoSource.IsRunning)
        {
            resetRequested = true;
            Clear();
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

        bool changed;
        lock (resultSync)
        {
            // Not published after disabled
            if (!enabled)
            {
                return;
            }

            changed = (frame.Width != info.FrameWidth) || (frame.Height != info.FrameHeight) || !workMask.AsSpan().SequenceEqual(mask);
            workMask.CopyTo(mask, 0);
            info = new MotionInfo(frame.Sequence, frame.Width, frame.Height, result.Detected, result.Level, result.Cells);
        }

        // Posting to the dispatcher allocates, so the overlay is not notified while the cells stay the same
        if (changed)
        {
            Updated?.Invoke(this, EventArgs.Empty);
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
