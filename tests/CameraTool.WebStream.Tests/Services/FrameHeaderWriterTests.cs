namespace CameraTool.WebStream.Services;

using System.Buffers.Binary;

using CameraTool.Capture;
using CameraTool.WebStream.Components;
using CameraTool.WebStream.Settings;

using Microsoft.Extensions.Logging.Abstractions;

public sealed class FrameHeaderWriterTests
{
    // Not created, the area is not saved in the test
    private static readonly string AreaPath = Path.Combine(Path.GetTempPath(), "CameraTool.WebStream.Tests", Guid.NewGuid().ToString("N"), "area.json");

    [Fact]
    public void WritesHeaderBeforeData()
    {
        // Arrange (only the motion detection is enabled, the services are created but not started)
        var hubs = new StreamHubs();
        using var source = new VideoSource(NullLogger<VideoSource>.Instance, new CameraSettings(), new TestCameraProvider());
        var area = new AreaService(NullLogger<AreaService>.Instance, new AreaSettings { Path = AreaPath });
        var detectSettings = new DetectSettings { Enable = false };
        var motion = new MotionService(NullLogger<MotionService>.Instance, new MotionSettings(), detectSettings, source, hubs, area);
        var gate = new DetectionGate(NullLogger<DetectionGate>.Instance, detectSettings, motion);
        using var detection = new FaceDetectionService(NullLogger<FaceDetectionService>.Instance, detectSettings, source, hubs, gate, area);
        using var objectDetection = new ObjectDetectionService(NullLogger<ObjectDetectionService>.Instance, new ObjectSettings(), source, hubs, gate, area);
        var writer = new FrameHeaderWriter(detection, motion, objectDetection);

        var frame = new FramePool().Rent();
        byte[] data = [0xFF, 0xD8, 0x01, 0x02, 0xFF, 0xD9];
        data.CopyTo(frame.GetDataBuffer(data.Length));
        var captureTime = CaptureClock.Now - TimeSpan.FromMilliseconds(40);

        // Act
        var writeTime = writer.Write(frame, 5, new FrameHeaderInfo(640, 480, 77, captureTime, true, false), data.Length);

        // Assert
        Assert.Equal(5, frame.Index);
        Assert.True(frame.IsKeyFrame);
        Assert.Equal(data, frame.Data.ToArray());
        var message = frame.Message.ToArray();
        var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(message.AsSpan(2));
        Assert.Equal(message.Length - data.Length, headerLength);
        Assert.Equal(data, message[headerLength..]);
        Assert.Equal(77u, BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(8)));
        // Motion section (bit 1) and key frame (bit 3)
        Assert.Equal(0xAu, BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(12)));
        // The capture time is the encoded time minus the time since the capture
        var sinceCapture = (writeTime - captureTime).TotalMilliseconds;
        var difference = BinaryPrimitives.ReadDoubleLittleEndian(message.AsSpan(24)) - BinaryPrimitives.ReadDoubleLittleEndian(message.AsSpan(16));
        Assert.InRange(difference, sinceCapture - 0.01, sinceCapture + 0.01);
        frame.Release();
    }
}
