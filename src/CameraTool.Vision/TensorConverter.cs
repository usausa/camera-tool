namespace CameraTool.Vision;

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

// Resize (bilinear) and convert to a CHW RGB tensor normalized by (x - 127) / 128.
// The vector version gathers the samples with scalar loads (no gather instructions) and computes 8 pixels at once,
// in the same order as the scalar version so that the results are identical.
public static unsafe class TensorConverter
{
    private const int Channels = 3;

    private const float Mean = 127f;

    private const float Scale = 1f / 128f;

    private const int VectorFloats = 8;

    // Columns sampled at once
    private const int ChunkColumns = 64;

    // Column tables are allocated on the stack
    private const int MaxVectorWidth = 2048;

    //--------------------------------------------------------------------------------
    // BGRA
    //--------------------------------------------------------------------------------

    public static void BgraToTensor(ReadOnlySpan<byte> bgra, int stride, int width, int height, Span<float> destination, int destinationWidth, int destinationHeight) =>
        BgraToTensor(bgra, stride, width, height, destination, destinationWidth, destinationHeight, Avx2.IsSupported);

    internal static void BgraToTensor(ReadOnlySpan<byte> bgra, int stride, int width, int height, Span<float> destination, int destinationWidth, int destinationHeight, bool simd)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if ((stride < width * 4) || (bgra.Length < RequiredLength(stride, width * 4, height)))
        {
            throw new ArgumentException("Invalid source.", nameof(bgra));
        }

        ValidateDestination(destination, destinationWidth, destinationHeight);

        fixed (byte* source = bgra)
        {
            fixed (float* tensor = destination)
            {
                if (simd && (destinationWidth <= MaxVectorWidth))
                {
                    BgraToTensorVector(source, stride, width, height, tensor, destinationWidth, destinationHeight);
                }
                else
                {
                    BgraToTensorScalar(source, stride, width, height, tensor, destinationWidth, destinationHeight);
                }
            }
        }
    }

    private static void BgraToTensorScalar(byte* source, int stride, int width, int height, float* tensor, int destinationWidth, int destinationHeight)
    {
        var planeSize = destinationWidth * destinationHeight;
        var scaleX = (float)width / destinationWidth;
        var scaleY = (float)height / destinationHeight;
        var r = tensor;
        var g = tensor + planeSize;
        var b = tensor + (planeSize * 2);
        for (var dy = 0; dy < destinationHeight; dy++)
        {
            Map(dy, scaleY, height, out var y0, out var y1, out var fy);
            var row0 = source + (y0 * stride);
            var row1 = source + (y1 * stride);
            var index = dy * destinationWidth;
            for (var dx = 0; dx < destinationWidth; dx++)
            {
                Map(dx, scaleX, width, out var x0, out var x1, out var fx);
                var p00 = row0 + (x0 * 4);
                var p01 = row0 + (x1 * 4);
                var p10 = row1 + (x0 * 4);
                var p11 = row1 + (x1 * 4);
                b[index] = Normalize(Lerp(p00[0], p01[0], p10[0], p11[0], fx, fy));
                g[index] = Normalize(Lerp(p00[1], p01[1], p10[1], p11[1], fx, fy));
                r[index] = Normalize(Lerp(p00[2], p01[2], p10[2], p11[2], fx, fy));
                index++;
            }
        }
    }

    private static void BgraToTensorVector(byte* source, int stride, int width, int height, float* tensor, int destinationWidth, int destinationHeight)
    {
        var planeSize = destinationWidth * destinationHeight;
        var scaleY = (float)height / destinationHeight;
        var x0 = stackalloc int[destinationWidth];
        var x1 = stackalloc int[destinationWidth];
        var fx = stackalloc float[destinationWidth];
        MapColumns(x0, x1, fx, destinationWidth, (float)width / destinationWidth, width);

        // Corners of each channel (B, G, R)
        var samples = stackalloc float[ChunkColumns * 4 * Channels];
        var blue = new Corners(samples, 0);
        var green = new Corners(samples, 4);
        var red = new Corners(samples, 8);

        for (var dy = 0; dy < destinationHeight; dy++)
        {
            Map(dy, scaleY, height, out var y0, out var y1, out var fy);
            var row0 = source + (y0 * stride);
            var row1 = source + (y1 * stride);
            var index = dy * destinationWidth;
            for (var start = 0; start < destinationWidth; start += ChunkColumns)
            {
                var count = Math.Min(ChunkColumns, destinationWidth - start);
                for (var i = 0; i < count; i++)
                {
                    var p00 = row0 + (x0[start + i] * 4);
                    var p01 = row0 + (x1[start + i] * 4);
                    var p10 = row1 + (x0[start + i] * 4);
                    var p11 = row1 + (x1[start + i] * 4);
                    blue.Set(i, p00[0], p01[0], p10[0], p11[0]);
                    green.Set(i, p00[1], p01[1], p10[1], p11[1]);
                    red.Set(i, p00[2], p01[2], p10[2], p11[2]);
                }

                var offset = index + start;
                WriteBilinear(tensor + offset, red, fx + start, fy, count);
                WriteBilinear(tensor + planeSize + offset, green, fx + start, fy, count);
                WriteBilinear(tensor + (planeSize * 2) + offset, blue, fx + start, fy, count);
            }
        }
    }

    //--------------------------------------------------------------------------------
    // NV12
    //--------------------------------------------------------------------------------

    public static void Nv12ToTensor(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, int width, int height, YuvRange range, Span<float> destination, int destinationWidth, int destinationHeight) =>
        Nv12ToTensor(y, yStride, uv, uvStride, width, height, range, destination, destinationWidth, destinationHeight, Avx2.IsSupported);

    internal static void Nv12ToTensor(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, int width, int height, YuvRange range, Span<float> destination, int destinationWidth, int destinationHeight, bool simd)
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

        ValidateDestination(destination, destinationWidth, destinationHeight);

        var coefficient = YuvCoefficient.Get(range);
        fixed (byte* yPointer = y)
        {
            fixed (byte* uvPointer = uv)
            {
                fixed (float* tensor = destination)
                {
                    if (simd && (destinationWidth <= MaxVectorWidth))
                    {
                        Nv12ToTensorVector(yPointer, yStride, uvPointer, uvStride, width, height, coefficient, tensor, destinationWidth, destinationHeight);
                    }
                    else
                    {
                        Nv12ToTensorScalar(yPointer, yStride, uvPointer, uvStride, width, height, coefficient, tensor, destinationWidth, destinationHeight);
                    }
                }
            }
        }
    }

    private static void Nv12ToTensorScalar(byte* yPointer, int yStride, byte* uvPointer, int uvStride, int width, int height, YuvCoefficient coefficient, float* tensor, int destinationWidth, int destinationHeight)
    {
        var planeSize = destinationWidth * destinationHeight;
        var scaleX = (float)width / destinationWidth;
        var scaleY = (float)height / destinationHeight;
        for (var dy = 0; dy < destinationHeight; dy++)
        {
            Map(dy, scaleY, height, out var y0, out var y1, out var fy);
            var row0 = yPointer + (y0 * yStride);
            var row1 = yPointer + (y1 * yStride);
            var chromaRow = uvPointer + ((y0 >> 1) * uvStride);
            var index = dy * destinationWidth;
            for (var dx = 0; dx < destinationWidth; dx++)
            {
                Map(dx, scaleX, width, out var x0, out var x1, out var fx);
                var luma = Lerp(row0[x0], row0[x1], row1[x0], row1[x1], fx, fy);
                var chroma = chromaRow + (x0 & ~1);
                WriteYuv(tensor, index, planeSize, luma, chroma[0], chroma[1], coefficient);
                index++;
            }
        }
    }

    private static void Nv12ToTensorVector(byte* yPointer, int yStride, byte* uvPointer, int uvStride, int width, int height, YuvCoefficient coefficient, float* tensor, int destinationWidth, int destinationHeight)
    {
        var planeSize = destinationWidth * destinationHeight;
        var scaleY = (float)height / destinationHeight;
        var x0 = stackalloc int[destinationWidth];
        var x1 = stackalloc int[destinationWidth];
        var fx = stackalloc float[destinationWidth];
        MapColumns(x0, x1, fx, destinationWidth, (float)width / destinationWidth, width);

        var samples = stackalloc float[ChunkColumns * 6];
        var luma = new Corners(samples, 0);
        var u = samples + (ChunkColumns * 4);
        var v = samples + (ChunkColumns * 5);

        for (var dy = 0; dy < destinationHeight; dy++)
        {
            Map(dy, scaleY, height, out var y0, out var y1, out var fy);
            var row0 = yPointer + (y0 * yStride);
            var row1 = yPointer + (y1 * yStride);
            var chromaRow = uvPointer + ((y0 >> 1) * uvStride);
            var index = dy * destinationWidth;
            for (var start = 0; start < destinationWidth; start += ChunkColumns)
            {
                var count = Math.Min(ChunkColumns, destinationWidth - start);
                for (var i = 0; i < count; i++)
                {
                    var c0 = x0[start + i];
                    var c1 = x1[start + i];
                    luma.Set(i, row0[c0], row0[c1], row1[c0], row1[c1]);
                    var chroma = chromaRow + (c0 & ~1);
                    u[i] = chroma[0];
                    v[i] = chroma[1];
                }

                WriteYuvVector(tensor + index + start, planeSize, luma, u, v, fx + start, fy, count, coefficient);
            }
        }
    }

    //--------------------------------------------------------------------------------
    // YUY2
    //--------------------------------------------------------------------------------

    public static void Yuy2ToTensor(ReadOnlySpan<byte> yuy2, int stride, int width, int height, YuvRange range, Span<float> destination, int destinationWidth, int destinationHeight) =>
        Yuy2ToTensor(yuy2, stride, width, height, range, destination, destinationWidth, destinationHeight, Avx2.IsSupported);

    internal static void Yuy2ToTensor(ReadOnlySpan<byte> yuy2, int stride, int width, int height, YuvRange range, Span<float> destination, int destinationWidth, int destinationHeight, bool simd)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var rowBytes = ((width + 1) / 2) * 4;
        if ((stride < rowBytes) || (yuy2.Length < RequiredLength(stride, rowBytes, height)))
        {
            throw new ArgumentException("Invalid source.", nameof(yuy2));
        }

        ValidateDestination(destination, destinationWidth, destinationHeight);

        var coefficient = YuvCoefficient.Get(range);
        fixed (byte* source = yuy2)
        {
            fixed (float* tensor = destination)
            {
                if (simd && (destinationWidth <= MaxVectorWidth))
                {
                    Yuy2ToTensorVector(source, stride, width, height, coefficient, tensor, destinationWidth, destinationHeight);
                }
                else
                {
                    Yuy2ToTensorScalar(source, stride, width, height, coefficient, tensor, destinationWidth, destinationHeight);
                }
            }
        }
    }

    private static void Yuy2ToTensorScalar(byte* source, int stride, int width, int height, YuvCoefficient coefficient, float* tensor, int destinationWidth, int destinationHeight)
    {
        var planeSize = destinationWidth * destinationHeight;
        var scaleX = (float)width / destinationWidth;
        var scaleY = (float)height / destinationHeight;
        for (var dy = 0; dy < destinationHeight; dy++)
        {
            Map(dy, scaleY, height, out var y0, out var y1, out var fy);
            var row0 = source + (y0 * stride);
            var row1 = source + (y1 * stride);
            var index = dy * destinationWidth;
            for (var dx = 0; dx < destinationWidth; dx++)
            {
                Map(dx, scaleX, width, out var x0, out var x1, out var fx);
                var luma = Lerp(row0[x0 * 2], row0[x1 * 2], row1[x0 * 2], row1[x1 * 2], fx, fy);
                var pair = row0 + ((x0 >> 1) * 4);
                WriteYuv(tensor, index, planeSize, luma, pair[1], pair[3], coefficient);
                index++;
            }
        }
    }

    private static void Yuy2ToTensorVector(byte* source, int stride, int width, int height, YuvCoefficient coefficient, float* tensor, int destinationWidth, int destinationHeight)
    {
        var planeSize = destinationWidth * destinationHeight;
        var scaleY = (float)height / destinationHeight;
        var x0 = stackalloc int[destinationWidth];
        var x1 = stackalloc int[destinationWidth];
        var fx = stackalloc float[destinationWidth];
        MapColumns(x0, x1, fx, destinationWidth, (float)width / destinationWidth, width);

        var samples = stackalloc float[ChunkColumns * 6];
        var luma = new Corners(samples, 0);
        var u = samples + (ChunkColumns * 4);
        var v = samples + (ChunkColumns * 5);

        for (var dy = 0; dy < destinationHeight; dy++)
        {
            Map(dy, scaleY, height, out var y0, out var y1, out var fy);
            var row0 = source + (y0 * stride);
            var row1 = source + (y1 * stride);
            var index = dy * destinationWidth;
            for (var start = 0; start < destinationWidth; start += ChunkColumns)
            {
                var count = Math.Min(ChunkColumns, destinationWidth - start);
                for (var i = 0; i < count; i++)
                {
                    var c0 = x0[start + i];
                    var c1 = x1[start + i];
                    luma.Set(i, row0[c0 * 2], row0[c1 * 2], row1[c0 * 2], row1[c1 * 2]);
                    var pair = row0 + ((c0 >> 1) * 4);
                    u[i] = pair[1];
                    v[i] = pair[3];
                }

                WriteYuvVector(tensor + index + start, planeSize, luma, u, v, fx + start, fy, count, coefficient);
            }
        }
    }

    //--------------------------------------------------------------------------------
    // Vector
    //--------------------------------------------------------------------------------

    private static void MapColumns(int* x0, int* x1, float* fx, int count, float scale, int size)
    {
        for (var i = 0; i < count; i++)
        {
            Map(i, scale, size, out x0[i], out x1[i], out fx[i]);
        }
    }

    // Bilinear interpolation and normalization of a channel
    private static void WriteBilinear(float* destination, Corners corners, float* fx, float fy, int count)
    {
        var vfy = Vector256.Create(fy);
        var i = 0;
        for (; i <= count - VectorFloats; i += VectorFloats)
        {
            Avx.Store(destination + i, Normalize(Lerp(corners, i, Avx.LoadVector256(fx + i), vfy)));
        }

        for (; i < count; i++)
        {
            destination[i] = Normalize(Lerp(corners.V00[i], corners.V01[i], corners.V10[i], corners.V11[i], fx[i], fy));
        }
    }

    // Bilinear luma, nearest chroma and the color conversion
    private static void WriteYuvVector(float* destination, int planeSize, Corners luma, float* u, float* v, float* fx, float fy, int count, YuvCoefficient k)
    {
        var vfy = Vector256.Create(fy);
        var yOffset = Vector256.Create(k.YOffset);
        var yScale = Vector256.Create(k.YScale);
        var rv = Vector256.Create(k.Rv);
        var gu = Vector256.Create(k.Gu);
        var gv = Vector256.Create(k.Gv);
        var bu = Vector256.Create(k.Bu);
        var center = Vector256.Create(128f);
        var i = 0;
        for (; i <= count - VectorFloats; i += VectorFloats)
        {
            var c = (Lerp(luma, i, Avx.LoadVector256(fx + i), vfy) - yOffset) * yScale;
            var d = Avx.LoadVector256(u + i) - center;
            var e = Avx.LoadVector256(v + i) - center;
            Avx.Store(destination + i, Normalize(c + (rv * e)));
            Avx.Store(destination + planeSize + i, Normalize(c - (gu * d) - (gv * e)));
            Avx.Store(destination + (planeSize * 2) + i, Normalize(c + (bu * d)));
        }

        for (; i < count; i++)
        {
            var value = Lerp(luma.V00[i], luma.V01[i], luma.V10[i], luma.V11[i], fx[i], fy);
            WriteYuv(destination, i, planeSize, value, u[i], v[i], k);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<float> Lerp(Corners corners, int index, Vector256<float> fx, Vector256<float> fy)
    {
        var v00 = Avx.LoadVector256(corners.V00 + index);
        var v10 = Avx.LoadVector256(corners.V10 + index);
        var top = v00 + ((Avx.LoadVector256(corners.V01 + index) - v00) * fx);
        var bottom = v10 + ((Avx.LoadVector256(corners.V11 + index) - v10) * fx);
        return top + ((bottom - top) * fy);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<float> Normalize(Vector256<float> value) =>
        (Avx.Min(Avx.Max(value, Vector256<float>.Zero), Vector256.Create(255f)) - Vector256.Create(Mean)) * Vector256.Create(Scale);

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static int RequiredLength(int stride, int rowBytes, int rows) => ((rows - 1) * stride) + rowBytes;

    private static void ValidateDestination(Span<float> destination, int destinationWidth, int destinationHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(destinationWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(destinationHeight);
        if (destination.Length < Channels * destinationWidth * destinationHeight)
        {
            throw new ArgumentException("Invalid destination.", nameof(destination));
        }
    }

    // Center aligned sampling position
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Map(int index, float scale, int size, out int i0, out int i1, out float fraction)
    {
        var position = Math.Clamp(((index + 0.5f) * scale) - 0.5f, 0f, size - 1);
        i0 = (int)position;
        i1 = Math.Min(i0 + 1, size - 1);
        fraction = position - i0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Lerp(float v00, float v01, float v10, float v11, float fx, float fy)
    {
        var top = v00 + ((v01 - v00) * fx);
        var bottom = v10 + ((v11 - v10) * fx);
        return top + ((bottom - top) * fy);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Normalize(float value) => (Math.Clamp(value, 0f, 255f) - Mean) * Scale;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteYuv(float* tensor, int index, int planeSize, float y, float u, float v, YuvCoefficient k)
    {
        var c = (y - k.YOffset) * k.YScale;
        var d = u - 128f;
        var e = v - 128f;
        tensor[index] = Normalize(c + (k.Rv * e));
        tensor[planeSize + index] = Normalize(c - (k.Gu * d) - (k.Gv * e));
        tensor[(planeSize * 2) + index] = Normalize(c + (k.Bu * d));
    }

    // Four corner samples of a chunk (stack memory)
    private readonly struct Corners
    {
        public float* V00 { get; }

        public float* V01 { get; }

        public float* V10 { get; }

        public float* V11 { get; }

        // index: first of the four arrays in the buffer
        public Corners(float* buffer, int index)
        {
            V00 = buffer + (index * ChunkColumns);
            V01 = buffer + ((index + 1) * ChunkColumns);
            V10 = buffer + ((index + 2) * ChunkColumns);
            V11 = buffer + ((index + 3) * ChunkColumns);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int index, float v00, float v01, float v10, float v11)
        {
            V00[index] = v00;
            V01[index] = v01;
            V10[index] = v10;
            V11[index] = v11;
        }
    }

    // BT.601
    private readonly struct YuvCoefficient
    {
        private static readonly YuvCoefficient Limited = new(16f, 255f / 219f, 1.596027f, 0.391762f, 0.812968f, 2.017232f);

        private static readonly YuvCoefficient Full = new(0f, 1f, 1.402f, 0.344136f, 0.714136f, 1.772f);

        public float YOffset { get; }

        public float YScale { get; }

        public float Rv { get; }

        public float Gu { get; }

        public float Gv { get; }

        public float Bu { get; }

        private YuvCoefficient(float yOffset, float yScale, float rv, float gu, float gv, float bu)
        {
            YOffset = yOffset;
            YScale = yScale;
            Rv = rv;
            Gu = gu;
            Gv = gv;
            Bu = bu;
        }

        public static YuvCoefficient Get(YuvRange range) => range == YuvRange.Full ? Full : Limited;
    }
}
