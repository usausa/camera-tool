namespace CameraTool.WebStream.Services;

// Decides which H.264 frames a client is sent. A frame depends on the previous frames,
// so a new client starts from a key frame, and a client that skipped a frame waits for the next key frame.
// Used by one connection.
public sealed class KeyFrameGate
{
    private bool waiting = true;

    // lastIndex: index of the last frame sent or skipped.
    // False when the frame is not sent (the caller skips it and requests a key frame).
    public bool CanSend(long index, bool keyFrame, long lastIndex)
    {
        if (index != lastIndex + 1)
        {
            waiting = true;
        }

        if (waiting && !keyFrame)
        {
            return false;
        }

        waiting = false;
        return true;
    }
}
