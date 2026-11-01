namespace CameraTool.Capture;

public sealed class VideoCaptureFailedEventArgs : EventArgs
{
    public string Message { get; }

    public uint Code { get; }

    public VideoCaptureFailedEventArgs(string message, uint code)
    {
        Message = message;
        Code = code;
    }
}
