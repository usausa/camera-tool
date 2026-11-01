namespace CameraTool.Vision;

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

// YUV (BT.601) to BGRA
public static unsafe class ImageConverter
{
    private const int BytesPerPixel = 4;

    private const int VectorPixels = 8;

    //--------------------------------------------------------------------------------
    // NV12
    //--------------------------------------------------------------------------------

    public static void Nv12ToBgra(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, Span<byte> bgra, int bgraStride, int width, int height, YuvRange range) =>
        Nv12ToBgra(y, yStride, uv, uvStride, bgra, bgraStride, width, height, range, Avx2.IsSupported);

    internal static void Nv12ToBgra(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, Span<byte> bgra, int bgraStride, int width, int height, YuvRange range, bool simd)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var uvRowBytes = ((width + 1) / 2) * 2;
        if ((yStride < width) || (y.Length < RequiredLength(yStride, width, height)))
        {
            throw new ArgumentException("Invalid Y plane.", nameof(y));
        }

        if ((uvStride < uvRowBytes) || (uv.Length < RequiredLength(uvStride, uvRowBytes, (height + 1) / 2)))
        {
            throw new ArgumentException("Invalid UV plane.", nameof(uv));
        }

        if ((bgraStride < width * BytesPerPixel) || (bgra.Length < RequiredLength(bgraStride, width * BytesPerPixel, height)))
        {
            throw new ArgumentException("Invalid destination.", nameof(bgra));
        }

        var coefficient = Coefficient.Get(range);
        fixed (byte* yPointer = y)
        {
            fixed (byte* uvPointer = uv)
            {
                fixed (byte* bgraPointer = bgra)
                {
                    for (var row = 0; row < height; row++)
                    {
                        var ySource = yPointer + (row * yStride);
                        var uvSource = uvPointer + ((row >> 1) * uvStride);
                        var destination = bgraPointer + (row * bgraStride);

                        var x = simd ? Nv12RowAvx2(ySource, uvSource, destination, width, coefficient) : 0;
                        for (; x < width; x++)
                        {
                            var chroma = uvSource + (x & ~1);
                            *(uint*)(destination + (x * BytesPerPixel)) = ToBgra(ySource[x], chroma[0], chroma[1], coefficient);
                        }
                    }
                }
            }
        }
    }

    private static int Nv12RowAvx2(byte* y, byte* uv, byte* destination, int width, Coefficient coefficient)
    {
        var vectorCoefficient = new VectorCoefficient(coefficient);
        var uIndex = Vector256.Create(0, 0, 2, 2, 4, 4, 6, 6);
        var vIndex = Vector256.Create(1, 1, 3, 3, 5, 5, 7, 7);

        var x = 0;
        for (; x <= width - VectorPixels; x += VectorPixels)
        {
            var yValue = Avx2.ConvertToVector256Int32(y + x);
            var uvValue = Avx2.ConvertToVector256Int32(uv + x);
            var uValue = Avx2.PermuteVar8x32(uvValue, uIndex);
            var vValue = Avx2.PermuteVar8x32(uvValue, vIndex);
            Avx.Store((int*)(destination + (x * BytesPerPixel)), ToBgra(yValue, uValue, vValue, vectorCoefficient));
        }

        return x;
    }

    //--------------------------------------------------------------------------------
    // YUY2
    //--------------------------------------------------------------------------------

    public static void Yuy2ToBgra(ReadOnlySpan<byte> yuy2, int yuy2Stride, Span<byte> bgra, int bgraStride, int width, int height, YuvRange range) =>
        Yuy2ToBgra(yuy2, yuy2Stride, bgra, bgraStride, width, height, range, Avx2.IsSupported);

    internal static void Yuy2ToBgra(ReadOnlySpan<byte> yuy2, int yuy2Stride, Span<byte> bgra, int bgraStride, int width, int height, YuvRange range, bool simd)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var rowBytes = ((width + 1) / 2) * 4;
        if ((yuy2Stride < rowBytes) || (yuy2.Length < RequiredLength(yuy2Stride, rowBytes, height)))
        {
            throw new ArgumentException("Invalid source.", nameof(yuy2));
        }

        if ((bgraStride < width * BytesPerPixel) || (bgra.Length < RequiredLength(bgraStride, width * BytesPerPixel, height)))
        {
            throw new ArgumentException("Invalid destination.", nameof(bgra));
        }

        var coefficient = Coefficient.Get(range);
        fixed (byte* sourcePointer = yuy2)
        {
            fixed (byte* bgraPointer = bgra)
            {
                for (var row = 0; row < height; row++)
                {
                    var source = sourcePointer + (row * yuy2Stride);
                    var destination = bgraPointer + (row * bgraStride);

                    var x = simd ? Yuy2RowAvx2(source, destination, width, coefficient) : 0;
                    for (; x < width; x++)
                    {
                        var pair = source + ((x >> 1) * 4);
                        *(uint*)(destination + (x * BytesPerPixel)) = ToBgra(source[x * 2], pair[1], pair[3], coefficient);
                    }
                }
            }
        }
    }

    private static int Yuy2RowAvx2(byte* source, byte* destination, int width, Coefficient coefficient)
    {
        var vectorCoefficient = new VectorCoefficient(coefficient);
        // Y0 U0 Y1 V0 Y2 U1 Y3 V1 ...
        var yShuffle = Vector128.Create(0, 2, 4, 6, 8, 10, 12, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);
        var uShuffle = Vector128.Create(1, 1, 5, 5, 9, 9, 13, 13, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);
        var vShuffle = Vector128.Create(3, 3, 7, 7, 11, 11, 15, 15, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);

        var x = 0;
        for (; x <= width - VectorPixels; x += VectorPixels)
        {
            var value = Sse2.LoadVector128(source + (x * 2));
            var yValue = Avx2.ConvertToVector256Int32(Ssse3.Shuffle(value, yShuffle));
            var uValue = Avx2.ConvertToVector256Int32(Ssse3.Shuffle(value, uShuffle));
            var vValue = Avx2.ConvertToVector256Int32(Ssse3.Shuffle(value, vShuffle));
            Avx.Store((int*)(destination + (x * BytesPerPixel)), ToBgra(yValue, uValue, vValue, vectorCoefficient));
        }

        return x;
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static int RequiredLength(int stride, int rowBytes, int rows) => ((rows - 1) * stride) + rowBytes;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ToBgra(int y, int u, int v, Coefficient k)
    {
        var c = (k.Y * (y - k.YOffset)) + 128;
        var d = u - 128;
        var e = v - 128;
        var r = Clamp((c + (k.Rv * e)) >> 8);
        var g = Clamp((c - (k.Gu * d) - (k.Gv * e)) >> 8);
        var b = Clamp((c + (k.Bu * d)) >> 8);
        return b | (g << 8) | (r << 16) | 0xFF000000u;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Clamp(int value) => (uint)Math.Clamp(value, 0, 255);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> ToBgra(Vector256<int> y, Vector256<int> u, Vector256<int> v, VectorCoefficient k)
    {
        var c = ((y - k.YOffset) * k.Y) + k.Round;
        var d = u - k.Center;
        var e = v - k.Center;
        var r = Clamp((c + (e * k.Rv)) >> 8);
        var g = Clamp((c - (d * k.Gu) - (e * k.Gv)) >> 8);
        var b = Clamp((c + (d * k.Bu)) >> 8);
        return b | (g << 8) | (r << 16) | Vector256.Create(unchecked((int)0xFF000000));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> Clamp(Vector256<int> value) =>
        Vector256.Min(Vector256.Max(value, Vector256<int>.Zero), Vector256.Create(255));

    // Fixed point (x256) BT.601 coefficients
    private readonly struct Coefficient
    {
        private static readonly Coefficient Limited = new(16, 298, 409, 100, 208, 516);

        private static readonly Coefficient Full = new(0, 256, 359, 88, 183, 454);

        public int YOffset { get; }

        public int Y { get; }

        public int Rv { get; }

        public int Gu { get; }

        public int Gv { get; }

        public int Bu { get; }

        private Coefficient(int yOffset, int y, int rv, int gu, int gv, int bu)
        {
            YOffset = yOffset;
            Y = y;
            Rv = rv;
            Gu = gu;
            Gv = gv;
            Bu = bu;
        }

        public static Coefficient Get(YuvRange range) => range == YuvRange.Full ? Full : Limited;
    }

    private readonly struct VectorCoefficient
    {
        public Vector256<int> YOffset { get; }

        public Vector256<int> Y { get; }

        public Vector256<int> Rv { get; }

        public Vector256<int> Gu { get; }

        public Vector256<int> Gv { get; }

        public Vector256<int> Bu { get; }

        public Vector256<int> Round { get; }

        public Vector256<int> Center { get; }

        public VectorCoefficient(Coefficient coefficient)
        {
            YOffset = Vector256.Create(coefficient.YOffset);
            Y = Vector256.Create(coefficient.Y);
            Rv = Vector256.Create(coefficient.Rv);
            Gu = Vector256.Create(coefficient.Gu);
            Gv = Vector256.Create(coefficient.Gv);
            Bu = Vector256.Create(coefficient.Bu);
            Round = Vector256.Create(128);
            Center = Vector256.Create(128);
        }
    }
}
