namespace CameraTool.FaceViewer.Components;

using System.Diagnostics.CodeAnalysis;

using CameraTool.Capture;
using CameraTool.Vision;

public sealed class FrameSlot
{
    private const int BytesPerPixel = 4;

    private byte[] buffer = [];

    // BGRA
    public Span<byte> Data => buffer;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int Stride => Width * BytesPerPixel;

    public long Sequence { get; set; }

    public TimeSpan CaptureTime { get; set; }

    public TimeSpan ArrivalTime { get; set; }

    internal void Prepare(int width, int height)
    {
        var size = width * height * BytesPerPixel;
        if (buffer.Length != size)
        {
            buffer = GC.AllocateUninitializedArray<byte>(size);
        }

        Width = width;
        Height = height;
    }
}

// Lock-free triple buffer (single producer, single consumer)
public sealed class FrameStore
{
    private const int IndexMask = 0x3;

    private const int NewFrameFlag = 0x4;

    private readonly FrameSlot[] slots = [new(), new(), new()];

    private readonly LatencyCounter displayLatency = new();

    // Producer only
    private int writeIndex;

    // Consumer only
    private int readIndex = 1;

    // Shared
    private int readyIndex = 2;

    private long displayedFrames;

    // Raised on the producer thread
    public event EventHandler? FrameWritten;

    public long DisplayedFrames => Interlocked.Read(ref displayedFrames);

    //--------------------------------------------------------------------------------
    // Producer
    //--------------------------------------------------------------------------------

    public FrameSlot BeginWrite(int width, int height)
    {
        var slot = slots[writeIndex];
        slot.Prepare(width, height);
        return slot;
    }

    public void EndWrite()
    {
        writeIndex = Interlocked.Exchange(ref readyIndex, writeIndex | NewFrameFlag) & IndexMask;
        FrameWritten?.Invoke(this, EventArgs.Empty);
    }

    //--------------------------------------------------------------------------------
    // Consumer
    //--------------------------------------------------------------------------------

    public bool TryAcquire([NotNullWhen(true)] out FrameSlot? slot)
    {
        if ((Volatile.Read(ref readyIndex) & NewFrameFlag) == 0)
        {
            slot = null;
            return false;
        }

        readIndex = Interlocked.Exchange(ref readyIndex, readIndex) & IndexMask;
        slot = slots[readIndex];
        return true;
    }

    public void ReportDisplayed(FrameSlot slot)
    {
        displayLatency.Add(CaptureClock.Now - slot.ArrivalTime);
        Interlocked.Increment(ref displayedFrames);
    }

    public LatencySnapshot TakeDisplayLatency() => displayLatency.Take();
}
