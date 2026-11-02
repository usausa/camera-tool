namespace CameraTool.Vision;

using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

// Detects motion from the difference between the luma (downscaled to 160x120) and its running average.
// The result is a 32x24 cell mask (row major, the lowest bit first) of the cells in the area. Not thread safe.
public sealed unsafe class MotionDetector
{
    public const int Width = 160;

    public const int Height = 120;

    public const int Columns = 32;

    public const int Rows = 24;

    public const int CellCount = Columns * Rows;

    public const int MaskLength = CellCount / 8;

    private const int CellWidth = Width / Columns;

    private const int CellHeight = Height / Rows;

    // Rows summed in ushort
    private const int MaxRowsPerPixel = ushort.MaxValue / byte.MaxValue;

    private readonly byte[] luma = new byte[Width * Height];

    private readonly float[] background = new float[Width * Height];

    private readonly int[] cellCounts = new int[CellCount];

    private readonly int threshold;

    private readonly int cellThreshold;

    private readonly float minArea;

    private readonly float rate;

    private ushort[] columnSums = [];

    private bool initialized;

    private int lastWidth;

    private int lastHeight;

    // Cells outside the area are not counted, and the level and the minimum area are relative to the cells in the area.
    // The background is updated outside the area too, so the area can be changed at any time.
    public DetectionArea Area { get; set; } = DetectionArea.Full;

    // Downscaled luma of the last frame
    internal ReadOnlySpan<byte> Luma => luma;

    public MotionDetector(MotionDetectorOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(options.Threshold);
        threshold = options.Threshold;
        cellThreshold = Math.Max(1, (int)MathF.Ceiling(options.CellRatio * CellWidth * CellHeight));
        minArea = options.MinArea;
        rate = Math.Clamp(options.BackgroundRate, 0f, 1f);
    }

    // The next frame initializes the background
    public void Reset() => initialized = false;

    //--------------------------------------------------------------------------------
    // Input
    //--------------------------------------------------------------------------------

    public MotionResult DetectNv12(ReadOnlySpan<byte> y, int stride, int width, int height, Span<byte> mask) =>
        DetectNv12(y, stride, width, height, mask, Avx2.IsSupported);

    internal MotionResult DetectNv12(ReadOnlySpan<byte> y, int stride, int width, int height, Span<byte> mask, bool simd)
    {
        Validate(y, stride, width, width, height, mask, nameof(y));
        fixed (byte* source = y)
        {
            Downscale(source, stride, width, height, LumaLayout.Plane, simd);
        }

        return Compare(width, height, mask);
    }

    public MotionResult DetectYuy2(ReadOnlySpan<byte> yuy2, int stride, int width, int height, Span<byte> mask) =>
        DetectYuy2(yuy2, stride, width, height, mask, Avx2.IsSupported);

    internal MotionResult DetectYuy2(ReadOnlySpan<byte> yuy2, int stride, int width, int height, Span<byte> mask, bool simd)
    {
        Validate(yuy2, stride, ((width + 1) / 2) * 4, width, height, mask, nameof(yuy2));
        fixed (byte* source = yuy2)
        {
            Downscale(source, stride, width, height, LumaLayout.Yuy2, simd);
        }

        return Compare(width, height, mask);
    }

    // Luma is approximated by (B + 2G + R) / 4
    public MotionResult DetectBgra(ReadOnlySpan<byte> bgra, int stride, int width, int height, Span<byte> mask)
    {
        Validate(bgra, stride, width * 4, width, height, mask, nameof(bgra));
        fixed (byte* source = bgra)
        {
            Downscale(source, stride, width, height, LumaLayout.Bgra, false);
        }

        return Compare(width, height, mask);
    }

    private static void Validate(ReadOnlySpan<byte> source, int stride, int rowBytes, int width, int height, Span<byte> mask, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, Height * MaxRowsPerPixel);
        if ((stride < rowBytes) || (source.Length < ((height - 1) * stride) + rowBytes))
        {
            throw new ArgumentException("Invalid source.", name);
        }

        if (mask.Length < MaskLength)
        {
            throw new ArgumentException("Invalid mask.", nameof(mask));
        }
    }

    //--------------------------------------------------------------------------------
    // Downscale (area average)
    //--------------------------------------------------------------------------------

    private void Downscale(byte* source, int stride, int width, int height, LumaLayout layout, bool simd)
    {
        if (columnSums.Length < width)
        {
            columnSums = new ushort[width];
        }

        fixed (ushort* sums = columnSums)
        {
            for (var oy = 0; oy < Height; oy++)
            {
                var y0 = (oy * height) / Height;
                var y1 = Math.Max(y0 + 1, ((oy + 1) * height) / Height);

                // Column sums of the rows
                new Span<ushort>(sums, width).Clear();
                for (var y = y0; y < y1; y++)
                {
                    AddRow(source + (y * stride), sums, width, layout, simd);
                }

                var rows = y1 - y0;
                var destination = oy * Width;
                for (var ox = 0; ox < Width; ox++)
                {
                    var x0 = (ox * width) / Width;
                    var x1 = Math.Max(x0 + 1, ((ox + 1) * width) / Width);
                    var sum = 0;
                    for (var x = x0; x < x1; x++)
                    {
                        sum += sums[x];
                    }

                    var count = rows * (x1 - x0);
                    luma[destination + ox] = (byte)((sum + (count / 2)) / count);
                }
            }
        }
    }

    private static void AddRow(byte* row, ushort* sums, int width, LumaLayout layout, bool simd)
    {
        var x = 0;
        switch (layout)
        {
            case LumaLayout.Plane:
                if (simd)
                {
                    for (; x <= width - 16; x += 16)
                    {
                        var value = Avx2.ConvertToVector256Int16(Sse2.LoadVector128(row + x)).AsUInt16();
                        Avx.Store(sums + x, Avx2.Add(Avx.LoadVector256(sums + x), value));
                    }
                }

                for (; x < width; x++)
                {
                    sums[x] += row[x];
                }

                break;
            case LumaLayout.Yuy2:
                if (simd)
                {
                    // Y0 U0 Y1 V0 ... as ushort: the low byte is Y
                    var lowByte = Vector256.Create((ushort)0x00FF);
                    for (; x <= width - 16; x += 16)
                    {
                        var value = Avx2.And(Avx.LoadVector256((ushort*)(row + (x * 2))), lowByte);
                        Avx.Store(sums + x, Avx2.Add(Avx.LoadVector256(sums + x), value));
                    }
                }

                for (; x < width; x++)
                {
                    sums[x] += row[x * 2];
                }

                break;
            default:
                for (; x < width; x++)
                {
                    var pixel = row + (x * 4);
                    sums[x] += (ushort)((pixel[0] + (pixel[1] * 2) + pixel[2] + 2) >> 2);
                }

                break;
        }
    }

    //--------------------------------------------------------------------------------
    // Compare with the background
    //--------------------------------------------------------------------------------

    private MotionResult Compare(int width, int height, Span<byte> mask)
    {
        mask[..MaskLength].Clear();

        // The first frame (or a new size) initializes the background
        if (!initialized || (width != lastWidth) || (height != lastHeight))
        {
            for (var i = 0; i < luma.Length; i++)
            {
                background[i] = luma[i];
            }

            initialized = true;
            lastWidth = width;
            lastHeight = height;
            return default;
        }

        Array.Clear(cellCounts);
        for (var y = 0; y < Height; y++)
        {
            var cellRow = (y / CellHeight) * Columns;
            var offset = y * Width;
            for (var x = 0; x < Width; x++)
            {
                var value = (float)luma[offset + x];
                var average = background[offset + x];
                if (MathF.Abs(value - average) > threshold)
                {
                    cellCounts[cellRow + (x / CellWidth)]++;
                }

                background[offset + x] = average + ((value - average) * rate);
            }
        }

        for (var i = 0; i < CellCount; i++)
        {
            if (cellCounts[i] >= cellThreshold)
            {
                mask[i >> 3] |= (byte)(1 << (i & 7));
            }
        }

        // Only the cells in the area are counted
        var area = Area;
        var areaMask = area.Mask;
        var cells = 0;
        for (var i = 0; i < MaskLength; i++)
        {
            mask[i] &= areaMask[i];
            cells += BitOperations.PopCount(mask[i]);
        }

        var areaCells = area.CellCount;
        var minCells = Math.Max(1, (int)MathF.Ceiling(minArea * areaCells));
        return new MotionResult(cells >= minCells, areaCells > 0 ? (float)cells / areaCells : 0, cells);
    }

    private enum LumaLayout
    {
        Plane,
        Yuy2,
        Bgra
    }
}
