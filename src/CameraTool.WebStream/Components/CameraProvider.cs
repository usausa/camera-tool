namespace CameraTool.WebStream.Components;

using CameraTool.Capture;

// Cameras used by VideoSource (the tests provide a camera of their own)
public interface ICameraProvider
{
    Task<IReadOnlyList<VideoDevice>> GetDevicesAsync();

    IVideoCapture CreateCapture(string deviceId);
}

// Cameras of MediaFrameReader
public sealed class CameraProvider : ICameraProvider
{
    public Task<IReadOnlyList<VideoDevice>> GetDevicesAsync() => VideoDeviceCatalog.GetDevicesAsync();

    public IVideoCapture CreateCapture(string deviceId) => new VideoCapture(deviceId);
}
