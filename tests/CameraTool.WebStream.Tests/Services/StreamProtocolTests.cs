namespace CameraTool.WebStream.Services;

using System.Buffers.Binary;
using System.Text;

using CameraTool.Vision;

public sealed class StreamProtocolTests
{
    private const int BaseLength = 32;

    private const int FaceSectionLength = 8;

    private const int FaceLength = 20;

    private const int MotionSectionLength = 8 + MotionDetector.MaskLength;

    private const int ObjectSectionLength = 8;

    private const int ObjectLength = 24;

    //--------------------------------------------------------------------------------
    // WebSocket
    //--------------------------------------------------------------------------------

    [Fact]
    public void WritesBaseHeader()
    {
        // Arrange
        var header = new FrameHeader
        {
            Width = 640,
            Height = 480,
            Sequence = 123,
            CaptureTime = 1_000.5,
            EncodedTime = 1_010.25,
            KeyFrame = true,
            FullRange = true
        };

        // Act
        var bytes = Write(header);

        // Assert
        Assert.Equal(BaseLength, bytes.Length);
        Assert.Equal(StreamProtocol.HeaderVersion, ReadUInt16(bytes, 0));
        Assert.Equal(BaseLength, ReadUInt16(bytes, 2));
        Assert.Equal(640, ReadUInt16(bytes, 4));
        Assert.Equal(480, ReadUInt16(bytes, 6));
        Assert.Equal(123, ReadUInt32(bytes, 8));
        // Key frame (bit 3) and full range (bit 4)
        Assert.Equal(0x18, ReadUInt32(bytes, 12));
        Assert.Equal(1_000.5, ReadDouble(bytes, 16));
        Assert.Equal(1_010.25, ReadDouble(bytes, 24));
    }

    [Fact]
    public void WritesSectionsInOrder()
    {
        // Arrange
        FaceBox[] faces = [new(0.1f, 0.2f, 0.3f, 0.4f, 0.9f), new(0.5f, 0.6f, 0.7f, 0.8f, 0.75f)];
        var mask = new byte[MotionDetector.MaskLength];
        mask[0] = 0x01;
        mask[^1] = 0x80;
        ObjectBox[] objects = [new(0.25f, 0.5f, 0.75f, 1f, 0.625f, 16)];
        var header = new FrameHeader
        {
            Width = 1280,
            Height = 720,
            Sequence = 10,
            Detection = new DetectionInfo(8, 1280, 720, faces.Length),
            Faces = faces,
            Motion = new MotionInfo(9, 0.5f, 2),
            MotionMask = mask,
            ObjectDetection = new DetectionInfo(7, 1280, 720, objects.Length),
            Objects = objects
        };

        // Act
        var bytes = Write(header);

        // Assert
        const int faceSection = BaseLength;
        const int motionSection = faceSection + FaceSectionLength + (FaceLength * 2);
        const int objectSection = motionSection + MotionSectionLength;
        Assert.Equal(objectSection + ObjectSectionLength + ObjectLength, bytes.Length);
        Assert.Equal(bytes.Length, ReadUInt16(bytes, 2));
        // Face (bit 0), motion (bit 1) and object (bit 2) sections
        Assert.Equal(0x7, ReadUInt32(bytes, 12));

        Assert.Equal(8, ReadUInt32(bytes, faceSection));
        Assert.Equal(2, ReadUInt16(bytes, faceSection + 4));
        const int secondFace = faceSection + FaceSectionLength + FaceLength;
        Assert.Equal(0.5f, ReadSingle(bytes, secondFace));
        Assert.Equal(0.8f, ReadSingle(bytes, secondFace + 12));
        Assert.Equal(0.75f, ReadSingle(bytes, secondFace + 16));

        Assert.Equal(9, ReadUInt32(bytes, motionSection));
        Assert.Equal(2, ReadUInt16(bytes, motionSection + 4));
        Assert.Equal(MotionDetector.Columns, bytes[motionSection + 6]);
        Assert.Equal(MotionDetector.Rows, bytes[motionSection + 7]);
        Assert.Equal(mask, bytes[(motionSection + 8)..(motionSection + 8 + MotionDetector.MaskLength)]);

        Assert.Equal(7, ReadUInt32(bytes, objectSection));
        Assert.Equal(1, ReadUInt16(bytes, objectSection + 4));
        const int firstObject = objectSection + ObjectSectionLength;
        Assert.Equal(0.25f, ReadSingle(bytes, firstObject));
        Assert.Equal(1f, ReadSingle(bytes, firstObject + 12));
        Assert.Equal(0.625f, ReadSingle(bytes, firstObject + 16));
        Assert.Equal(16, ReadUInt16(bytes, firstObject + 20));
    }

    [Fact]
    public void WritesOnlyCountedBoxes()
    {
        // Arrange (the result buffer is longer than the count)
        var faces = new FaceBox[FaceDetectionService.MaxFaces];
        faces[0] = new FaceBox(0.1f, 0.1f, 0.2f, 0.2f, 0.9f);
        faces[1] = new FaceBox(0.3f, 0.3f, 0.4f, 0.4f, 0.8f);
        var header = new FrameHeader { Detection = new DetectionInfo(1, 640, 480, 1), Faces = faces };

        // Act
        var bytes = Write(header);

        // Assert
        Assert.Equal(BaseLength + FaceSectionLength + FaceLength, bytes.Length);
        Assert.Equal(0x1, ReadUInt32(bytes, 12));
        Assert.Equal(0.9f, ReadSingle(bytes, BaseLength + FaceSectionLength + 16));
    }

    [Fact]
    public void MaxHeaderLengthHoldsAllSections()
    {
        // Arrange
        var header = new FrameHeader
        {
            Detection = new DetectionInfo(1, 640, 480, FaceDetectionService.MaxFaces),
            Faces = new FaceBox[FaceDetectionService.MaxFaces],
            Motion = new MotionInfo(1, 0f, 0),
            MotionMask = new byte[MotionDetector.MaskLength],
            ObjectDetection = new DetectionInfo(1, 640, 480, ObjectDetectionService.MaxObjects),
            Objects = new ObjectBox[ObjectDetectionService.MaxObjects]
        };

        // Assert
        Assert.Equal(StreamProtocol.MaxHeaderLength, StreamProtocol.GetHeaderLength(header));
    }

    //--------------------------------------------------------------------------------
    // MJPEG
    //--------------------------------------------------------------------------------

    [Fact]
    public void MjpegPartEndsWithNextPartHeader()
    {
        // The header of the next part follows each JPEG, so a browser displays the frame without waiting for the next one
        Assert.Equal("multipart/x-mixed-replace; boundary=frame", StreamProtocol.MjpegContentType);
        Assert.Equal("--frame\r\nContent-Type: image/jpeg\r\n\r\n", Encoding.ASCII.GetString(StreamProtocol.FirstPartHeader));
        Assert.Equal("\r\n--frame\r\nContent-Type: image/jpeg\r\n\r\n", Encoding.ASCII.GetString(StreamProtocol.NextPartHeader));
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static byte[] Write(in FrameHeader header)
    {
        var bytes = new byte[StreamProtocol.GetHeaderLength(header)];
        StreamProtocol.WriteHeader(bytes, header);
        return bytes;
    }

    private static int ReadUInt16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));

    private static long ReadUInt32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static float ReadSingle(byte[] bytes, int offset) => BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset));

    private static double ReadDouble(byte[] bytes, int offset) => BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(offset));
}
