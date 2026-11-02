namespace CameraTool.Vision;

using System.Runtime.InteropServices;

// WIC JPEG encoder called through the COM vtable.
// Full range NV12 is passed as YCbCr planes without color conversion, and the data is written to the given buffer.
public sealed unsafe partial class JpegEncoder : IDisposable
{
    // IWICImagingFactory
    private const int CreateEncoderSlot = 8;
    private const int CreateStreamSlot = 14;
    // IWICStream (IStream)
    private const int SeekSlot = 5;
    private const int InitializeFromMemorySlot = 16;
    // IWICBitmapEncoder
    private const int EncoderInitializeSlot = 3;
    private const int CreateNewFrameSlot = 10;
    private const int EncoderCommitSlot = 11;
    // IPropertyBag2
    private const int WriteSlot = 4;
    // IWICBitmapFrameEncode
    private const int FrameInitializeSlot = 3;
    private const int SetSizeSlot = 4;
    private const int SetResolutionSlot = 5;
    private const int SetPixelFormatSlot = 6;
    private const int FrameCommitSlot = 12;
    // IWICPlanarBitmapFrameEncode
    private const int WritePixelsSlot = 3;

    private const uint ClsctxInprocServer = 1;
    private const int EncoderNoCache = 2;
    private const uint StreamSeekCurrent = 1;
    private const ushort VariantSingle = 4;
    private const ushort VariantByte = 17;
    private const byte Subsampling420 = 1;
    private const int PlaneCount = 2;
    private const double Dpi = 96;
    private const int HeaderAllowance = 4096;

    private static readonly Guid ClsidImagingFactory = new("317d06e8-5f24-433d-bdf7-79ce68d8abc2");
    private static readonly Guid IidImagingFactory = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
    private static readonly Guid IidPlanarFrameEncode = new("f928b7b8-2221-40c1-b72e-7e82f1974d1a");
    private static readonly Guid ContainerFormatJpeg = new("19e4a5aa-5662-4fc5-a0c0-1758028e1057");
    private static readonly Guid PixelFormat24bppBgr = new("6fddc324-4e03-4bfe-b185-3d77768dc90c");
    private static readonly Guid PixelFormat8bppY = new("91b4db54-2df9-42f0-b449-2909bb3df88e");
    private static readonly Guid PixelFormat16bppCbCr = new("ff95ba6e-11e0-4263-bb45-01721f3460a4");

    private readonly float quality;

    private IntPtr factory;

    public JpegEncoder(int quality)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);
        this.quality = quality / 100f;
        Marshal.ThrowExceptionForHR(CoCreateInstance(in ClsidImagingFactory, IntPtr.Zero, ClsctxInprocServer, in IidImagingFactory, out factory));
    }

    public void Dispose()
    {
        if (factory != IntPtr.Zero)
        {
            Marshal.Release(factory);
            factory = IntPtr.Zero;
        }
    }

    // Upper bound of the encoded size
    public static int GetMaxLength(int width, int height) => (width * height * 3) + HeaderAllowance;

    // Encodes full range NV12 as 4:2:0 JPEG, returns false when the destination is too small
    public bool TryEncodeNv12(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, int width, int height, Span<byte> destination, out int written)
    {
        ObjectDisposedException.ThrowIf(factory == IntPtr.Zero, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var uvRowBytes = ((width + 1) / 2) * 2;
        var uvRows = (height + 1) / 2;
        if ((yStride < width) || (y.Length < ((height - 1) * yStride) + width))
        {
            throw new ArgumentException("Invalid Y plane.", nameof(y));
        }

        if ((uvStride < uvRowBytes) || (uv.Length < ((uvRows - 1) * uvStride) + uvRowBytes))
        {
            throw new ArgumentException("Invalid UV plane.", nameof(uv));
        }

        int result;
        int length;
        fixed (byte* yPointer = y)
        {
            fixed (byte* uvPointer = uv)
            {
                fixed (byte* destinationPointer = destination)
                {
                    var planes = stackalloc WicBitmapPlane[PlaneCount];
                    planes[0] = new WicBitmapPlane { Format = PixelFormat8bppY, Buffer = yPointer, Stride = (uint)yStride, BufferSize = (uint)y.Length };
                    planes[1] = new WicBitmapPlane { Format = PixelFormat16bppCbCr, Buffer = uvPointer, Stride = (uint)uvStride, BufferSize = (uint)uv.Length };
                    result = Encode(planes, width, height, destinationPointer, destination.Length, out length);
                }
            }
        }

        if ((result >= 0) && IsComplete(destination, length))
        {
            written = length;
            return true;
        }

        // A full buffer is reported as an error or as truncated data
        if (destination.Length < GetMaxLength(width, height))
        {
            written = 0;
            return false;
        }

        Marshal.ThrowExceptionForHR(result);
        throw new InvalidOperationException("JPEG encoding failed.");
    }

    private int Encode(WicBitmapPlane* planes, int width, int height, byte* destination, int capacity, out int length)
    {
        length = 0;
        var stream = IntPtr.Zero;
        var encoder = IntPtr.Zero;
        var frame = IntPtr.Zero;
        var options = IntPtr.Zero;
        var planar = IntPtr.Zero;
        try
        {
            // IWICImagingFactory.CreateStream, IWICStream.InitializeFromMemory
            var result = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(factory)[CreateStreamSlot])(factory, &stream);
            if (result < 0)
            {
                return result;
            }

            result = ((delegate* unmanaged[Stdcall]<IntPtr, byte*, uint, int>)Vtbl(stream)[InitializeFromMemorySlot])(stream, destination, (uint)capacity);
            if (result < 0)
            {
                return result;
            }

            // IWICImagingFactory.CreateEncoder, IWICBitmapEncoder.Initialize, IWICBitmapEncoder.CreateNewFrame
            var container = ContainerFormatJpeg;
            result = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, IntPtr*, int>)Vtbl(factory)[CreateEncoderSlot])(factory, &container, null, &encoder);
            if (result < 0)
            {
                return result;
            }

            result = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int>)Vtbl(encoder)[EncoderInitializeSlot])(encoder, stream, EncoderNoCache);
            if (result < 0)
            {
                return result;
            }

            result = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, IntPtr*, int>)Vtbl(encoder)[CreateNewFrameSlot])(encoder, &frame, &options);
            if (result < 0)
            {
                return result;
            }

            // IPropertyBag2.Write, IWICBitmapFrameEncode.Initialize / SetSize / SetResolution / SetPixelFormat
            result = WriteOptions(options);
            if (result < 0)
            {
                return result;
            }

            result = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Vtbl(frame)[FrameInitializeSlot])(frame, options);
            if (result < 0)
            {
                return result;
            }

            result = ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, int>)Vtbl(frame)[SetSizeSlot])(frame, (uint)width, (uint)height);
            if (result < 0)
            {
                return result;
            }

            result = ((delegate* unmanaged[Stdcall]<IntPtr, double, double, int>)Vtbl(frame)[SetResolutionSlot])(frame, Dpi, Dpi);
            if (result < 0)
            {
                return result;
            }

            // The interleaved format decides the accepted planar formats
            var pixelFormat = PixelFormat24bppBgr;
            result = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, int>)Vtbl(frame)[SetPixelFormatSlot])(frame, &pixelFormat);
            if (result < 0)
            {
                return result;
            }

            // IWICPlanarBitmapFrameEncode.WritePixels
            result = Marshal.QueryInterface(frame, in IidPlanarFrameEncode, out planar);
            if (result < 0)
            {
                return result;
            }

            result = ((delegate* unmanaged[Stdcall]<IntPtr, uint, WicBitmapPlane*, uint, int>)Vtbl(planar)[WritePixelsSlot])(planar, (uint)height, planes, PlaneCount);
            if (result < 0)
            {
                return result;
            }

            // IWICBitmapFrameEncode.Commit, IWICBitmapEncoder.Commit
            result = ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(frame)[FrameCommitSlot])(frame);
            if (result < 0)
            {
                return result;
            }

            result = ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(encoder)[EncoderCommitSlot])(encoder);
            if (result < 0)
            {
                return result;
            }

            // IStream.Seek (current position = encoded length)
            ulong position;
            result = ((delegate* unmanaged[Stdcall]<IntPtr, long, uint, ulong*, int>)Vtbl(stream)[SeekSlot])(stream, 0, StreamSeekCurrent, &position);
            if (result >= 0)
            {
                length = (int)position;
            }

            return result;
        }
        finally
        {
            Release(planar);
            Release(options);
            Release(frame);
            Release(encoder);
            Release(stream);
        }
    }

    private int WriteOptions(IntPtr options)
    {
        var names = stackalloc PropertyBagEntry[2];
        var values = stackalloc Variant[2];
        fixed (char* qualityName = "ImageQuality")
        {
            fixed (char* subsamplingName = "JpegYCrCbSubsampling")
            {
                names[0] = new PropertyBagEntry { Name = qualityName };
                names[1] = new PropertyBagEntry { Name = subsamplingName };
                values[0] = new Variant { Type = VariantSingle, Single = quality };
                values[1] = new Variant { Type = VariantByte, Byte = Subsampling420 };
                return ((delegate* unmanaged[Stdcall]<IntPtr, uint, PropertyBagEntry*, Variant*, int>)Vtbl(options)[WriteSlot])(options, 2, names, values);
            }
        }
    }

    // Ends with the EOI marker
    private static bool IsComplete(Span<byte> destination, int length) =>
        (length >= 4) && (length <= destination.Length) && (destination[length - 2] == 0xFF) && (destination[length - 1] == 0xD9);

    private static void** Vtbl(IntPtr pointer) => *(void***)pointer;

    private static void Release(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
        {
            Marshal.Release(pointer);
        }
    }

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr pointer);

    // PROPBAG2
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyBagEntry
    {
        public uint Type;
        public ushort VariantType;
        public ushort ClipboardFormat;
        public uint Hint;
        public char* Name;
        public Guid Clsid;
    }

    // VARIANT (VT_R4 / VT_UI1 only)
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Variant
    {
        [FieldOffset(0)]
        public ushort Type;

        [FieldOffset(8)]
        public float Single;

        [FieldOffset(8)]
        public byte Byte;
    }

    // WICBitmapPlane
    [StructLayout(LayoutKind.Sequential)]
    private struct WicBitmapPlane
    {
        public Guid Format;
        public byte* Buffer;
        public uint Stride;
        public uint BufferSize;
    }
}
