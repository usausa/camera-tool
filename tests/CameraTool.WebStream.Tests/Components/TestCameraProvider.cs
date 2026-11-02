namespace CameraTool.WebStream.Components;

using System.Diagnostics;
using System.Runtime.InteropServices;

using CameraTool.Capture;

// Camera of the tests: one device whose capture sends a moving gradient of YUY2 at the frame rate of the format
internal sealed class TestCameraProvider : ICameraProvider
{
    public const string DeviceName = "Test camera";

    public const int Width = 320;

    public const int Height = 240;

    public const int FrameRate = 30;

    private static readonly VideoDevice Device = new("test-camera", DeviceName, [new VideoFormat(VideoPixelFormat.Yuy2, Width, Height, FrameRate)]);

    private TestCapture? capture;

    // The capture created last (null while no camera has been opened)
    public TestCapture? Capture => Volatile.Read(ref capture);

    public Task<IReadOnlyList<VideoDevice>> GetDevicesAsync() => Task.FromResult<IReadOnlyList<VideoDevice>>([Device]);

    public IVideoCapture CreateCapture(string deviceId)
    {
        var created = new TestCapture();
        Volatile.Write(ref capture, created);
        return created;
    }
}

// Raises the frames on a dedicated thread in the same way as VideoCapture
internal sealed class TestCapture : IVideoCapture
{
    private readonly FrameCapturedEventArgs eventArgs = new();

    private readonly ManualResetEventSlim stopRequested = new();

    private VideoFormat format;

    // Pinned, the frames point to it
    private byte[] buffer = [];

    private Thread? thread;

    private int disposed;

    public event EventHandler<FrameCapturedEventArgs>? FrameCaptured;

    // The test camera does not fail
    public event EventHandler<VideoCaptureFailedEventArgs>? Failed
    {
        add
        {
        }

        remove
        {
        }
    }

    public VideoPixelFormat OutputPixelFormat => format.PixelFormat;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        stopRequested.Set();
        thread?.Join();
        stopRequested.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public Task OpenAsync(VideoFormat format, VideoPixelFormat outputPixelFormat = VideoPixelFormat.Unknown)
    {
        if ((format.PixelFormat != VideoPixelFormat.Yuy2) || (outputPixelFormat is not (VideoPixelFormat.Unknown or VideoPixelFormat.Yuy2)))
        {
            throw new InvalidOperationException("Format not supported.");
        }

        this.format = format;
        buffer = GC.AllocateArray<byte>(format.Width * format.Height * 2, true);
        return Task.CompletedTask;
    }

    public Task StartAsync()
    {
        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Test camera"
        };
        thread.Start();
        return Task.CompletedTask;
    }

    private void Run()
    {
        var pointer = Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0);
        var plane = new FramePlane(0, format.Width, format.Height, format.Width * 2);
        var interval = Stopwatch.Frequency / format.FrameRate;
        var start = Stopwatch.GetTimestamp();
        long sequence = 0;
        while (true)
        {
            Fill(sequence);
            sequence++;
            var now = CaptureClock.Now;
            eventArgs.Frame = new FrameBuffer(pointer, buffer.Length, VideoPixelFormat.Yuy2, VideoColorRange.Limited, format.Width, format.Height, plane, default, sequence, now, now);
            try
            {
                FrameCaptured?.Invoke(this, eventArgs);
            }
            finally
            {
                eventArgs.Frame = default;
            }

            // Waits for the time of the next frame
            var wait = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), start + (long)(sequence * interval));
            if (stopRequested.Wait(wait > TimeSpan.Zero ? wait : TimeSpan.Zero))
            {
                return;
            }
        }
    }

    // Diagonal gradient moving by 4 per frame, gray chroma (Y0 U Y1 V)
    private void Fill(long index)
    {
        var shift = (int)(index * 4);
        for (var y = 0; y < format.Height; y++)
        {
            var row = buffer.AsSpan(y * format.Width * 2, format.Width * 2);
            for (var x = 0; x < format.Width; x++)
            {
                row[x * 2] = (byte)(x + y + shift);
                row[(x * 2) + 1] = 128;
            }
        }
    }
}
