namespace CameraTool.Vision;

public readonly record struct LatencySnapshot(TimeSpan Average, TimeSpan Max);

// Milliseconds, formatted as "average / max" with the format of each number (e.g. "{0:F1} ms").
// A WPF binding can format it with StringFormat: a path to Average or Max would make the binding replace
// its listeners for each new value (about 2 KB per value).
public readonly record struct LatencyValue(double Average, double Max) : ISpanFormattable
{
    private const string Separator = " / ";

    public static LatencyValue From(LatencySnapshot snapshot) => new(snapshot.Average.TotalMilliseconds, snapshot.Max.TotalMilliseconds);

    public string ToString(string? format, IFormatProvider? formatProvider) =>
        String.Concat(Average.ToString(format, formatProvider), Separator, Max.ToString(format, formatProvider));

    // Used by String.Format without intermediate strings
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (Average.TryFormat(destination, out var averageLength, format, provider) &&
            Separator.AsSpan().TryCopyTo(destination[averageLength..]) &&
            Max.TryFormat(destination[(averageLength + Separator.Length)..], out var maxLength, format, provider))
        {
            charsWritten = averageLength + Separator.Length + maxLength;
            return true;
        }

        charsWritten = 0;
        return false;
    }
}

public sealed class LatencyCounter
{
    private readonly Lock sync = new();

    private long totalTicks;

    private long maxTicks;

    private int count;

    public void Add(TimeSpan value)
    {
        lock (sync)
        {
            totalTicks += value.Ticks;
            maxTicks = Math.Max(maxTicks, value.Ticks);
            count++;
        }
    }

    public LatencySnapshot Take()
    {
        lock (sync)
        {
            var snapshot = count > 0 ? new LatencySnapshot(TimeSpan.FromTicks(totalTicks / count), TimeSpan.FromTicks(maxTicks)) : default;
            totalTicks = 0;
            maxTicks = 0;
            count = 0;
            return snapshot;
        }
    }
}
