namespace CameraTool.Vision;

using System.Diagnostics.CodeAnalysis;
using System.Numerics;

// Cells of the detection area in the same grid as MotionDetector (32x24, row major, the lowest bit first).
// Immutable, so the capture and detection threads read it without a lock.
public sealed class DetectionArea
{
    public const int Columns = MotionDetector.Columns;

    public const int Rows = MotionDetector.Rows;

    private const char Included = '1';

    private const char Excluded = '0';

    private readonly byte[] mask;

    public static DetectionArea Full { get; } = new(CreateFullMask());

    // Cells in the area
    public int CellCount { get; }

    public bool IsFull => CellCount == MotionDetector.CellCount;

    public ReadOnlySpan<byte> Mask => mask;

    private DetectionArea(byte[] mask)
    {
        this.mask = mask;
        foreach (var value in mask)
        {
            CellCount += BitOperations.PopCount(value);
        }
    }

    private static byte[] CreateFullMask()
    {
        var mask = new byte[MotionDetector.MaskLength];
        mask.AsSpan().Fill(0xFF);
        return mask;
    }

    //--------------------------------------------------------------------------------
    // Cell
    //--------------------------------------------------------------------------------

    public bool Contains(int column, int row)
    {
        ValidateCell(column, row);
        return IsSet(mask, (row * Columns) + column);
    }

    // Normalized position in the frame (0-1), a position outside the frame is in the nearest cell
    public bool ContainsPoint(float x, float y) => IsSet(mask, (ToCell(y, Rows) * Columns) + ToCell(x, Columns));

    // Cells in the rectangle (both ends included, in any order) are set to the value
    public DetectionArea WithCells(int column0, int row0, int column1, int row1, bool included)
    {
        ValidateCell(column0, row0);
        ValidateCell(column1, row1);

        var result = (byte[])mask.Clone();
        for (var row = Math.Min(row0, row1); row <= Math.Max(row0, row1); row++)
        {
            for (var column = Math.Min(column0, column1); column <= Math.Max(column0, column1); column++)
            {
                var index = (row * Columns) + column;
                if (included)
                {
                    result[index >> 3] |= (byte)(1 << (index & 7));
                }
                else
                {
                    result[index >> 3] &= (byte)~(1 << (index & 7));
                }
            }
        }

        return new DetectionArea(result);
    }

    private static bool IsSet(ReadOnlySpan<byte> mask, int index) => (mask[index >> 3] & (1 << (index & 7))) != 0;

    private static int ToCell(float value, int count) => Math.Clamp((int)MathF.Floor(value * count), 0, count - 1);

    private static void ValidateCell(int column, int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Columns);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Rows);
    }

    //--------------------------------------------------------------------------------
    // Face and object
    //--------------------------------------------------------------------------------

    // Keeps the faces or the objects whose center is in the area at the start (in the same order) and returns the count
    public int Filter<T>(Span<T> boxes)
        where T : struct, IDetectionBox
    {
        if (IsFull)
        {
            return boxes.Length;
        }

        var count = 0;
        foreach (var box in boxes)
        {
            if (ContainsPoint((box.Left + box.Right) * 0.5f, (box.Top + box.Bottom) * 0.5f))
            {
                boxes[count++] = box;
            }
        }

        return count;
    }

    //--------------------------------------------------------------------------------
    // Text
    //--------------------------------------------------------------------------------

    // Rows from the top, one character per cell ('1': in the area, '0': outside)
    public string[] ToRows()
    {
        var rows = new string[Rows];
        for (var row = 0; row < Rows; row++)
        {
            rows[row] = String.Create(Columns, (mask, row), static (span, state) =>
            {
                for (var column = 0; column < span.Length; column++)
                {
                    span[column] = IsSet(state.mask, (state.row * Columns) + column) ? Included : Excluded;
                }
            });
        }

        return rows;
    }

    public static bool TryParse(IReadOnlyList<string>? rows, [NotNullWhen(true)] out DetectionArea? area)
    {
        area = null;
        if ((rows is null) || (rows.Count != Rows))
        {
            return false;
        }

        var result = new byte[MotionDetector.MaskLength];
        for (var row = 0; row < Rows; row++)
        {
            var text = rows[row];
            if ((text is null) || (text.Length != Columns))
            {
                return false;
            }

            for (var column = 0; column < Columns; column++)
            {
                var value = text[column];
                if (value == Included)
                {
                    var index = (row * Columns) + column;
                    result[index >> 3] |= (byte)(1 << (index & 7));
                }
                else if (value != Excluded)
                {
                    return false;
                }
            }
        }

        area = new DetectionArea(result);
        return true;
    }
}
