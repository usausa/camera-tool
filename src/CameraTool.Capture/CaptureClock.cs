namespace CameraTool.Capture;

using System.Diagnostics;

// Same time base as MediaFrameReference.SystemRelativeTime (QPC)
public static class CaptureClock
{
    private static readonly double TickFrequency = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;

    public static TimeSpan Now => FromTimestamp(Stopwatch.GetTimestamp());

    public static TimeSpan FromTimestamp(long timestamp) => new((long)(timestamp * TickFrequency));
}
