namespace CameraTool.WebStream.Services;

using System.Diagnostics;

using CameraTool.WebStream.Settings;

// Detect only on motion, shared by the face and object detection:
// while there is no motion in the area (after the hold time), the input is prepared only once per idle interval
public sealed class DetectionGate
{
    private readonly ILogger<DetectionGate> log;

    private readonly MotionService motion;

    private readonly TimeSpan idleInterval;

    private volatile bool enabled;

    public bool IsEnabled
    {
        get => enabled;
        set
        {
            enabled = value;
            log.InfoMotionGateChanged(value);
        }
    }

    public DetectionGate(ILogger<DetectionGate> log, DetectSettings settings, MotionService motion)
    {
        this.log = log;
        this.motion = motion;
        idleInterval = TimeSpan.FromMilliseconds(settings.IdleInterval);
        enabled = settings.MotionGate;
    }

    // lastPreparedTimestamp: Stopwatch timestamp of the last prepared input
    public bool IsIdle(long lastPreparedTimestamp) =>
        enabled &&
        motion.IsEnabled &&
        !motion.IsMoving &&
        (Stopwatch.GetElapsedTime(lastPreparedTimestamp) < idleInterval);
}
