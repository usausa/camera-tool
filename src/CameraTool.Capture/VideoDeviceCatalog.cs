namespace CameraTool.Capture;

using System.Runtime.InteropServices;

using Windows.Media.Capture;
using Windows.Media.Capture.Frames;

public static class VideoDeviceCatalog
{
    public static async Task<IReadOnlyList<VideoDevice>> GetDevicesAsync()
    {
        var groups = await MediaFrameSourceGroup.FindAllAsync().AsTask().ConfigureAwait(false);
        var devices = new List<VideoDevice>();
        foreach (var group in groups)
        {
            // Color only (exclude infrared and sensor groups)
            if ((group.SourceInfos.Count != 1) || (group.SourceInfos[0].SourceKind != MediaFrameSourceKind.Color))
            {
                continue;
            }

            var formats = await GetFormatsAsync(group).ConfigureAwait(false);
            if (formats.Count > 0)
            {
                devices.Add(new VideoDevice(group.Id, group.DisplayName, formats));
            }
        }

        return devices;
    }

    private static async Task<IReadOnlyList<VideoFormat>> GetFormatsAsync(MediaFrameSourceGroup group)
    {
        using var capture = new MediaCapture();
        try
        {
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = group,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video
            }).AsTask().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            return [];
        }

        var source = capture.FrameSources.Values.FirstOrDefault(static x => x.Info.SourceKind == MediaFrameSourceKind.Color);
        if (source is null)
        {
            return [];
        }

        return [.. source.SupportedFormats
            .Select(FormatHelper.ToVideoFormat)
            .Where(static x => x.PixelFormat != VideoPixelFormat.Unknown)
            .Distinct()
            .OrderBy(static x => x.PixelFormat)
            .ThenBy(static x => x.Width * x.Height)
            .ThenBy(static x => x.FrameRate)];
    }
}
