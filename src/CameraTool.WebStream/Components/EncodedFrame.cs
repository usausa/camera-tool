namespace CameraTool.WebStream.Components;

using System.Diagnostics;

// Encoded once and shared by all clients, returned to the pool when all references are released
public sealed class EncodedFrame
{
    private readonly FramePool pool;

    // The JPEG data follows the space reserved for the longest header, the header is placed just before it
    private byte[] buffer = [];

    private int headerLength;

    private int jpegLength;

    private int references;

    // Publish order
    public long Index { get; private set; }

    // Stopwatch timestamp
    public long PublishTimestamp { get; private set; }

    // WebSocket message (header + JPEG)
    public ReadOnlyMemory<byte> Message => buffer.AsMemory(StreamProtocol.MaxHeaderLength - headerLength, headerLength + jpegLength);

    public ReadOnlyMemory<byte> Jpeg => buffer.AsMemory(StreamProtocol.MaxHeaderLength, jpegLength);

    internal EncodedFrame(FramePool pool)
    {
        this.pool = pool;
    }

    // Space for the JPEG data
    internal Span<byte> GetJpegBuffer(int capacity)
    {
        if (buffer.Length < StreamProtocol.MaxHeaderLength + capacity)
        {
            buffer = GC.AllocateUninitializedArray<byte>(StreamProtocol.MaxHeaderLength + capacity);
        }

        return buffer.AsSpan(StreamProtocol.MaxHeaderLength);
    }

    // Space for the header just before the JPEG data
    internal Span<byte> GetHeaderBuffer(int length) => buffer.AsSpan(StreamProtocol.MaxHeaderLength - length, length);

    internal void Commit(long index, int headerLength, int jpegLength)
    {
        Index = index;
        this.headerLength = headerLength;
        this.jpegLength = jpegLength;
        PublishTimestamp = Stopwatch.GetTimestamp();
    }

    // Rented with one reference
    internal void Initialize() => references = 1;

    internal void AddReference() => Interlocked.Increment(ref references);

    public void Release()
    {
        var count = Interlocked.Decrement(ref references);
        Debug.Assert(count >= 0, "Released more than referenced.");
        if (count == 0)
        {
            pool.Return(this);
        }
    }
}

public sealed class FramePool
{
    private readonly Lock sync = new();

    private readonly Stack<EncodedFrame> frames = new();

    private int created;

    // Number of frames allocated
    public int Created => Volatile.Read(ref created);

    public EncodedFrame Rent()
    {
        EncodedFrame? frame;
        lock (sync)
        {
            frames.TryPop(out frame);
        }

        if (frame is null)
        {
            frame = new EncodedFrame(this);
            Interlocked.Increment(ref created);
        }

        frame.Initialize();
        return frame;
    }

    internal void Return(EncodedFrame frame)
    {
        lock (sync)
        {
            frames.Push(frame);
        }
    }
}
