namespace CameraTool.WebStream.Components;

using System.Buffers.Binary;
using System.Text;

using CameraTool.Vision;

public static class StreamProtocol
{
    //--------------------------------------------------------------------------------
    // WebSocket
    //--------------------------------------------------------------------------------

    // Binary message: header (little endian) + JPEG (/ws) or H.264 access unit in Annex B (/ws-h264)
    //  0 uint16  version
    //  2 uint16  header length (offset of the image data)
    //  4 uint16  width
    //  6 uint16  height
    //  8 uint32  sequence
    // 12 uint32  flags (bit 0: face section, bit 1: motion section, bit 2: object section, bit 3: H.264 key frame)
    // 16 float64 capture time (Unix ms)
    // 24 float64 encoded time (Unix ms)
    // Face section (while face detection is enabled)
    // +0 uint32  sequence of the frame the faces were detected in (0: not detected yet)
    // +4 uint16  face count
    // +6 uint16  reserved
    // +8 float32 x 5 for each face: left, top, right, bottom (0-1), confidence
    // Motion section (while motion detection is enabled), after the face section
    // +0 uint32  sequence of the frame the motion was detected in (0: not detected yet)
    // +4 uint16  moving cell count
    // +6 uint8   columns
    // +7 uint8   rows
    // +8 byte[]  cell mask (row major, the lowest bit first)
    // Object section (while object detection is enabled), after the motion section
    // +0 uint32  sequence of the frame the objects were detected in (0: not detected yet)
    // +4 uint16  object count
    // +6 uint16  reserved
    // +8 for each object: float32 x 5 (left, top, right, bottom (0-1), confidence), uint16 class id, uint16 reserved
    public const int HeaderVersion = 1;

    private const int BaseHeaderLength = 32;

    private const int FaceSectionLength = 8;

    private const int FaceLength = 20;

    private const int MotionSectionLength = 8 + MotionDetector.MaskLength;

    private const int ObjectSectionLength = 8;

    private const int ObjectLength = 24;

    private const uint FaceSectionFlag = 0x1;

    private const uint MotionSectionFlag = 0x2;

    private const uint ObjectSectionFlag = 0x4;

    private const uint KeyFrameFlag = 0x8;

    public const int MaxHeaderLength =
        BaseHeaderLength +
        FaceSectionLength + (FaceLength * FaceDetectionService.MaxFaces) +
        MotionSectionLength +
        ObjectSectionLength + (ObjectLength * ObjectDetectionService.MaxObjects);

    public static int GetHeaderLength(in FrameHeader header)
    {
        var length = BaseHeaderLength;
        if (header.Detection is { } detection)
        {
            length += FaceSectionLength + (FaceLength * detection.Count);
        }

        if (header.Motion.HasValue)
        {
            length += MotionSectionLength;
        }

        if (header.ObjectDetection is { } objectDetection)
        {
            length += ObjectSectionLength + (ObjectLength * objectDetection.Count);
        }

        return length;
    }

    // The destination length must be GetHeaderLength
    public static void WriteHeader(Span<byte> destination, in FrameHeader header)
    {
        var flags = (header.Detection.HasValue ? FaceSectionFlag : 0) |
            (header.Motion.HasValue ? MotionSectionFlag : 0) |
            (header.ObjectDetection.HasValue ? ObjectSectionFlag : 0) |
            (header.KeyFrame ? KeyFrameFlag : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(destination, HeaderVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], (ushort)destination.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], (ushort)header.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], (ushort)header.Height);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], (uint)header.Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], flags);
        BinaryPrimitives.WriteDoubleLittleEndian(destination[16..], header.CaptureTime);
        BinaryPrimitives.WriteDoubleLittleEndian(destination[24..], header.EncodedTime);

        var section = destination[BaseHeaderLength..];
        if (header.Detection is { } detection)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(section, (uint)detection.Sequence);
            BinaryPrimitives.WriteUInt16LittleEndian(section[4..], (ushort)detection.Count);
            BinaryPrimitives.WriteUInt16LittleEndian(section[6..], 0);
            var offset = FaceSectionLength;
            foreach (var face in header.Faces[..detection.Count])
            {
                BinaryPrimitives.WriteSingleLittleEndian(section[offset..], face.Left);
                BinaryPrimitives.WriteSingleLittleEndian(section[(offset + 4)..], face.Top);
                BinaryPrimitives.WriteSingleLittleEndian(section[(offset + 8)..], face.Right);
                BinaryPrimitives.WriteSingleLittleEndian(section[(offset + 12)..], face.Bottom);
                BinaryPrimitives.WriteSingleLittleEndian(section[(offset + 16)..], face.Confidence);
                offset += FaceLength;
            }

            section = section[offset..];
        }

        if (header.Motion is { } motion)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(section, (uint)motion.Sequence);
            BinaryPrimitives.WriteUInt16LittleEndian(section[4..], (ushort)motion.Cells);
            section[6] = MotionDetector.Columns;
            section[7] = MotionDetector.Rows;
            header.MotionMask[..MotionDetector.MaskLength].CopyTo(section[8..]);
            section = section[MotionSectionLength..];
        }

        if (header.ObjectDetection is { } objectDetection)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(section, (uint)objectDetection.Sequence);
            BinaryPrimitives.WriteUInt16LittleEndian(section[4..], (ushort)objectDetection.Count);
            BinaryPrimitives.WriteUInt16LittleEndian(section[6..], 0);
            var offset = ObjectSectionLength;
            foreach (var value in header.Objects[..objectDetection.Count])
            {
                BinaryPrimitives.WriteSingleLittleEndian(section[offset..], value.Left);
                BinaryPrimitives.WriteSingleLittleEndian(section[(offset + 4)..], value.Top);
                BinaryPrimitives.WriteSingleLittleEndian(section[(offset + 8)..], value.Right);
                BinaryPrimitives.WriteSingleLittleEndian(section[(offset + 12)..], value.Bottom);
                BinaryPrimitives.WriteSingleLittleEndian(section[(offset + 16)..], value.Confidence);
                BinaryPrimitives.WriteUInt16LittleEndian(section[(offset + 20)..], (ushort)value.ClassId);
                BinaryPrimitives.WriteUInt16LittleEndian(section[(offset + 22)..], 0);
                offset += ObjectLength;
            }
        }
    }

    //--------------------------------------------------------------------------------
    // MJPEG
    //--------------------------------------------------------------------------------

    // A browser displays a part when the header of the next part arrives, so the boundary and the next header
    // are written right after each JPEG (otherwise every frame is displayed one frame late).
    // The parts have no Content-Length because the next JPEG is not encoded yet.
    public const string Boundary = "frame";

    public const string MjpegContentType = "multipart/x-mixed-replace; boundary=" + Boundary;

    // CRLF + boundary + part header
    private static readonly byte[] PartDelimiter = Encoding.ASCII.GetBytes($"\r\n--{Boundary}\r\nContent-Type: image/jpeg\r\n\r\n");

    // Before the first JPEG
    public static ReadOnlySpan<byte> FirstPartHeader => PartDelimiter.AsSpan(2);

    // After each JPEG: ends the part and starts the next one
    public static ReadOnlySpan<byte> NextPartHeader => PartDelimiter;
}

// Contents of the WebSocket header
public readonly ref struct FrameHeader
{
    public int Width { get; init; }

    public int Height { get; init; }

    public long Sequence { get; init; }

    // Unix ms
    public double CaptureTime { get; init; }

    // Unix ms
    public double EncodedTime { get; init; }

    // H.264 frame that does not depend on the previous frames
    public bool KeyFrame { get; init; }

    // Face section (null: face detection is disabled)
    public FaceDetectionInfo? Detection { get; init; }

    public ReadOnlySpan<FaceBox> Faces { get; init; }

    // Motion section (null: motion detection is disabled)
    public MotionInfo? Motion { get; init; }

    public ReadOnlySpan<byte> MotionMask { get; init; }

    // Object section (null: object detection is disabled)
    public ObjectDetectionInfo? ObjectDetection { get; init; }

    public ReadOnlySpan<ObjectBox> Objects { get; init; }
}
