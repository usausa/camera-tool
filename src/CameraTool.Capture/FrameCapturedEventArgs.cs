namespace CameraTool.Capture;

// Reused for every frame to avoid allocation
public sealed class FrameCapturedEventArgs : EventArgs
{
    public FrameBuffer Frame { get; internal set; }
}
