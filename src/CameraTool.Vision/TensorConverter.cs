namespace CameraTool.Vision;

using System.Runtime.CompilerServices;

// Resize (bilinear) and convert to a CHW RGB tensor normalized by (x - 127) / 128
public static unsafe class TensorConverter
{
    private const int Channels = 3;

    private const float Mean = 127f;

    private const float Scale = 1f / 128f;

    //--------------------------------------------------------------------------------
    // BGRA
    //--------------------------------------------------------------------------------

    public static void BgraToTensor(ReadOnlySpan<byte> bgra, int stride, int width, int height, Span<float> destination, int destinationWidth, int destinationHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if ((stride < width * 4) || (bgra.Length < RequiredLength(stride, width * 4, height)))
        {
            throw new ArgumentException("Invalid source.", nameof(bgra));
        }

        ValidateDestination(destination, destinationWidth, destinationHeight);

        var planeSize = destinationWidth * destinationHeight;
        var scaleX = (float)width / destinationWidth;
        var scaleY = (float)height / destinationHeight;
        fixed (byte* source = bgra)
        {
            fixed (float* tensor = destination)
            {
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
        }
    }

    //--------------------------------------------------------------------------------
    // NV12
    //--------------------------------------------------------------------------------

    public static void Nv12ToTensor(ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> uv, int uvStride, int width, int height, YuvRange range, Span<float> destination, int destinationWidth, int destinationHeight)
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
        var planeSize = destinationWidth * destinationHeight;
        var scaleX = (float)width / destinationWidth;
        var scaleY = (float)height / destinationHeight;
        fixed (byte* yPointer = y)
        {
            fixed (byte* uvPointer = uv)
            {
                fixed (float* tensor = destination)
                {
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
            }
        }
    }

    //--------------------------------------------------------------------------------
    // YUY2
    //--------------------------------------------------------------------------------

    public static void Yuy2ToTensor(ReadOnlySpan<byte> yuy2, int stride, int width, int height, YuvRange range, Span<float> destination, int destinationWidth, int destinationHeight)
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
        var planeSize = destinationWidth * destinationHeight;
        var scaleX = (float)width / destinationWidth;
        var scaleY = (float)height / destinationHeight;
        fixed (byte* source = yuy2)
        {
            fixed (float* tensor = destination)
            {
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
        }
    }

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
    private static void WriteYuv(float* tensor, int index, int planeSize, float y, int u, int v, YuvCoefficient k)
    {
        var c = (y - k.YOffset) * k.YScale;
        var d = u - 128f;
        var e = v - 128f;
        tensor[index] = Normalize(c + (k.Rv * e));
        tensor[planeSize + index] = Normalize(c - (k.Gu * d) - (k.Gv * e));
        tensor[(planeSize * 2) + index] = Normalize(c + (k.Bu * d));
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
