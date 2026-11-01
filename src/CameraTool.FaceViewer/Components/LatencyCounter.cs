namespace CameraTool.FaceViewer.Components;

public readonly record struct LatencySnapshot(TimeSpan Average, TimeSpan Max);

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
