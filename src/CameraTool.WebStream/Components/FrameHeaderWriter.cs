namespace CameraTool.WebStream.Components;

using CameraTool.Capture;
using CameraTool.Vision;

// Frame values of the header (CaptureTime: capture clock time)
public readonly record struct FrameHeaderInfo(int Width, int Height, long Sequence, TimeSpan CaptureTime, bool KeyFrame, bool FullRange);

// Writes the WebSocket header with the latest detection results (faces, motion and objects) and commits the frame.
// Shared by the JPEG and the H.264 stream so that their headers stay the same.
// Each stream has its own writer, because the result buffers are used by the thread that writes.
public sealed class FrameHeaderWriter
{
    private readonly FaceDetectionService detection;

    private readonly MotionService motion;

    private readonly ObjectDetectionService objectDetection;

    private readonly FaceBox[] faces = new FaceBox[FaceDetectionService.MaxFaces];

    private readonly byte[] motionMask = new byte[MotionDetector.MaskLength];

    private readonly ObjectBox[] objects = new ObjectBox[ObjectDetectionService.MaxObjects];

    public FrameHeaderWriter(FaceDetectionService detection, MotionService motion, ObjectDetectionService objectDetection)
    {
        this.detection = detection;
        this.motion = motion;
        this.objectDetection = objectDetection;
    }

    // The data is already in the frame, returns the capture clock time of the write
    public TimeSpan Write(EncodedFrame encoded, long index, in FrameHeaderInfo info, int dataLength)
    {
        // Latest detection results, detected in a previous frame
        DetectionInfo? detected = detection.IsEnabled && detection.IsAvailable ? detection.CopyResults(faces) : null;
        MotionInfo? moved = motion.IsEnabled ? motion.CopyResults(motionMask) : null;
        DetectionInfo? found = objectDetection.IsEnabled && objectDetection.IsAvailable ? objectDetection.CopyResults(objects) : null;

        // Unix time of the capture, calculated from the capture clock
        var now = CaptureClock.Now;
        var unixNow = (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
        var header = new FrameHeader
        {
            Width = info.Width,
            Height = info.Height,
            Sequence = info.Sequence,
            CaptureTime = unixNow - (now - info.CaptureTime).TotalMilliseconds,
            EncodedTime = unixNow,
            KeyFrame = info.KeyFrame,
            FullRange = info.FullRange,
            Detection = detected,
            Faces = faces,
            Motion = moved,
            MotionMask = motionMask,
            ObjectDetection = found,
            Objects = objects
        };
        var headerLength = StreamProtocol.GetHeaderLength(header);
        StreamProtocol.WriteHeader(encoded.GetHeaderBuffer(headerLength), header);
        encoded.Commit(index, headerLength, dataLength, info.KeyFrame);
        return now;
    }
}
