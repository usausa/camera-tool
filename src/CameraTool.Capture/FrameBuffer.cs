namespace CameraTool.Capture;

// Valid only while the FrameCaptured handler is running
public readonly struct FrameBuffer : IEquatable<FrameBuffer>
{
    private readonly IntPtr buffer;

    public int Length { get; }

    public VideoPixelFormat PixelFormat { get; }

    public int Width { get; }

    public int Height { get; }

    public FramePlane Plane0 { get; }

    public FramePlane Plane1 { get; }

    public long Sequence { get; }

    public TimeSpan CaptureTime { get; }

    public TimeSpan ArrivalTime { get; }

    public bool IsEmpty => (buffer == IntPtr.Zero) || (Length == 0);

    public unsafe ReadOnlySpan<byte> Span => IsEmpty ? [] : new ReadOnlySpan<byte>((void*)buffer, Length);

    internal FrameBuffer(
        IntPtr buffer,
        int length,
        VideoPixelFormat pixelFormat,
        int width,
        int height,
        FramePlane plane0,
        FramePlane plane1,
        long sequence,
        TimeSpan captureTime,
        TimeSpan arrivalTime)
    {
        this.buffer = buffer;
        Length = length;
        PixelFormat = pixelFormat;
        Width = width;
        Height = height;
        Plane0 = plane0;
        Plane1 = plane1;
        Sequence = sequence;
        CaptureTime = captureTime;
        ArrivalTime = arrivalTime;
    }

    public ReadOnlySpan<byte> GetPlaneSpan(FramePlane plane)
    {
        var span = Span;
        var size = Math.Min(plane.Stride * plane.Height, span.Length - plane.Offset);
        return size > 0 ? span.Slice(plane.Offset, size) : [];
    }

    public override bool Equals(object? obj) => (obj is FrameBuffer other) && Equals(other);

    public bool Equals(FrameBuffer other) => (buffer == other.buffer) && (Sequence == other.Sequence);

    public override int GetHashCode() => HashCode.Combine(buffer, Sequence);

    public static bool operator ==(FrameBuffer left, FrameBuffer right) => left.Equals(right);

    public static bool operator !=(FrameBuffer left, FrameBuffer right) => !left.Equals(right);
}
