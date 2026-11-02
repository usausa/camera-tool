namespace CameraTool.Capture;

using System.Runtime.InteropServices;

using Windows.Graphics.Imaging;

// Reads the latest frame through the COM vtable without creating projection objects.
// Getting SoftwareBitmap through the projection adds GC memory pressure and induces gen2 GC.
internal unsafe ref struct NativeFrame
{
    // IInspectable based interfaces start at slot 6
    private const int TryAcquireLatestFrameSlot = 8;
    private const int SystemRelativeTimeSlot = 8;
    private const int VideoMediaFrameSlot = 12;
    private const int SoftwareBitmapSlot = 8;
    private const int BitmapPixelFormatSlot = 6;
    private const int PixelWidthSlot = 8;
    private const int PixelHeightSlot = 9;
    private const int LockBufferSlot = 15;
    private const int GetPlaneCountSlot = 6;
    private const int GetPlaneDescriptionSlot = 7;
    private const int CreateReferenceSlot = 6;
    private const int ReferenceValueSlot = 6;
    private const int CloseSlot = 6;
    // IUnknown based interface
    private const int GetBufferSlot = 3;

    private static readonly Guid IidMediaFrameReader = new("e4c94395-2028-48ed-90b0-d1c1b162e24c");
    private static readonly Guid IidMemoryBuffer = new("fbc4dd2a-245b-11e4-af98-689423260cf8");
    private static readonly Guid IidMemoryBufferByteAccess = new("5b0d3235-4dba-4d44-865e-8f1d0e4fd04d");
    private static readonly Guid IidClosable = new("30d5a829-7fa4-4026-83bb-d75bae4ea99e");

    private IntPtr frame;

    private IntPtr videoFrame;

    private IntPtr bitmap;

    private IntPtr bitmapBuffer;

    private IntPtr reference;

    public IntPtr Buffer { get; private set; }

    public int Capacity { get; private set; }

    public VideoPixelFormat PixelFormat { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public FramePlane Plane0 { get; private set; }

    public FramePlane Plane1 { get; private set; }

    public TimeSpan? CaptureTime { get; private set; }

    public readonly bool IsValid => Buffer != IntPtr.Zero;

    public static NativeFrame AcquireLatest(IntPtr reader)
    {
        var result = default(NativeFrame);
        if (Marshal.QueryInterface(reader, in IidMediaFrameReader, out var pointer) >= 0)
        {
            result.Acquire(pointer);
            Marshal.Release(pointer);
        }

        return result;
    }

    public void Dispose()
    {
        Release(ref reference, true);
        Release(ref bitmapBuffer, true);
        Release(ref bitmap, true);
        Release(ref videoFrame, false);
        Release(ref frame, true);
        Buffer = IntPtr.Zero;
    }

    private void Acquire(IntPtr reader)
    {
        IntPtr value;

        // IMediaFrameReader.TryAcquireLatestFrame
        if ((Invoke(reader, TryAcquireLatestFrameSlot, &value) < 0) || (value == IntPtr.Zero))
        {
            return;
        }

        frame = value;

        // IMediaFrameReference.SystemRelativeTime (IReference<TimeSpan>)
        if ((Invoke(frame, SystemRelativeTimeSlot, &value) >= 0) && (value != IntPtr.Zero))
        {
            long ticks;
            if (((delegate* unmanaged[Stdcall]<IntPtr, long*, int>)Vtbl(value)[ReferenceValueSlot])(value, &ticks) >= 0)
            {
                CaptureTime = new TimeSpan(ticks);
            }

            Marshal.Release(value);
        }

        // IMediaFrameReference.VideoMediaFrame
        if ((Invoke(frame, VideoMediaFrameSlot, &value) < 0) || (value == IntPtr.Zero))
        {
            return;
        }

        videoFrame = value;

        // IVideoMediaFrame.SoftwareBitmap
        if ((Invoke(videoFrame, SoftwareBitmapSlot, &value) < 0) || (value == IntPtr.Zero))
        {
            return;
        }

        bitmap = value;

        int pixelFormat;
        int width;
        int height;
        if ((InvokeInt(bitmap, BitmapPixelFormatSlot, &pixelFormat) < 0) ||
            (InvokeInt(bitmap, PixelWidthSlot, &width) < 0) ||
            (InvokeInt(bitmap, PixelHeightSlot, &height) < 0))
        {
            return;
        }

        // ISoftwareBitmap.LockBuffer
        if ((((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Vtbl(bitmap)[LockBufferSlot])(bitmap, (int)BitmapBufferAccessMode.Read, &value) < 0) ||
            (value == IntPtr.Zero))
        {
            return;
        }

        bitmapBuffer = value;

        // IBitmapBuffer.GetPlaneCount / GetPlaneDescription
        int planeCount;
        BitmapPlaneDescription plane0;
        var plane1 = default(BitmapPlaneDescription);
        if ((InvokeInt(bitmapBuffer, GetPlaneCountSlot, &planeCount) < 0) ||
            (GetPlaneDescription(bitmapBuffer, 0, &plane0) < 0) ||
            ((planeCount > 1) && (GetPlaneDescription(bitmapBuffer, 1, &plane1) < 0)))
        {
            return;
        }

        // IMemoryBuffer.CreateReference
        if (Marshal.QueryInterface(bitmapBuffer, in IidMemoryBuffer, out var memoryBuffer) < 0)
        {
            return;
        }

        var result = Invoke(memoryBuffer, CreateReferenceSlot, &value);
        Marshal.Release(memoryBuffer);
        if ((result < 0) || (value == IntPtr.Zero))
        {
            return;
        }

        reference = value;

        // IMemoryBufferByteAccess.GetBuffer
        if (Marshal.QueryInterface(reference, in IidMemoryBufferByteAccess, out var byteAccess) < 0)
        {
            return;
        }

        byte* data;
        uint capacity;
        result = ((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, int>)Vtbl(byteAccess)[GetBufferSlot])(byteAccess, &data, &capacity);
        Marshal.Release(byteAccess);
        if ((result < 0) || (data is null))
        {
            return;
        }

        Buffer = (IntPtr)data;
        Capacity = (int)capacity;
        PixelFormat = FormatHelper.FromBitmapPixelFormat((BitmapPixelFormat)pixelFormat);
        Width = width;
        Height = height;
        Plane0 = new FramePlane(plane0.StartIndex, plane0.Width, plane0.Height, plane0.Stride);
        Plane1 = planeCount > 1 ? new FramePlane(plane1.StartIndex, plane1.Width, plane1.Height, plane1.Stride) : default;
    }

    private static void** Vtbl(IntPtr pointer) => *(void***)pointer;

    private static int Invoke(IntPtr pointer, int slot, IntPtr* value) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(pointer)[slot])(pointer, value);

    private static int InvokeInt(IntPtr pointer, int slot, int* value) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Vtbl(pointer)[slot])(pointer, value);

    private static int GetPlaneDescription(IntPtr buffer, int index, BitmapPlaneDescription* value) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, int, BitmapPlaneDescription*, int>)Vtbl(buffer)[GetPlaneDescriptionSlot])(buffer, index, value);

    private static void Release(ref IntPtr pointer, bool close)
    {
        if (pointer == IntPtr.Zero)
        {
            return;
        }

        if (close && (Marshal.QueryInterface(pointer, in IidClosable, out var closable) >= 0))
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(closable)[CloseSlot])(closable);
            Marshal.Release(closable);
        }

        Marshal.Release(pointer);
        pointer = IntPtr.Zero;
    }
}
