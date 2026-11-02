namespace CameraTool.WebStream.Components;

using System.Buffers.Binary;
using System.Text;

using CameraTool.Vision;

public static class StreamProtocol
{
    //--------------------------------------------------------------------------------
    // WebSocket
    //--------------------------------------------------------------------------------

    // Binary message: header (little endian) + JPEG
    //  0 uint16  version
    //  2 uint16  header length (offset of the JPEG data)
    //  4 uint16  width
    //  6 uint16  height
    //  8 uint32  sequence
    // 12 uint32  flags (bit 0: face section)
    // 16 float64 capture time (Unix ms)
    // 24 float64 encoded time (Unix ms)
    // Face section (while face detection is enabled)
    // 32 uint32  sequence of the frame the faces were detected in (0: not detected yet)
    // 36 uint16  face count
    // 38 uint16  reserved
    // 40 float32 x 5 for each face: left, top, right, bottom (0-1), confidence
    public const int HeaderVersion = 1;

    private const int BaseHeaderLength = 32;

    private const int FaceSectionLength = 8;

    private const int FaceLength = 20;

    private const uint FaceSectionFlag = 0x1;

    public const int MaxHeaderLength = BaseHeaderLength + FaceSectionLength + (FaceLength * FaceDetectionService.MaxFaces);

    public static int GetHeaderLength(in FaceDetectionInfo? detection) =>
        detection is { } value ? BaseHeaderLength + FaceSectionLength + (FaceLength * value.Count) : BaseHeaderLength;

    // The destination length must be GetHeaderLength
    public static void WriteHeader(Span<byte> destination, int width, int height, long sequence, double captureTime, double encodedTime, in FaceDetectionInfo? detection, ReadOnlySpan<FaceBox> faces)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination, HeaderVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], (ushort)destination.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], (ushort)height);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], (uint)sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], detection.HasValue ? FaceSectionFlag : 0);
        BinaryPrimitives.WriteDoubleLittleEndian(destination[16..], captureTime);
        BinaryPrimitives.WriteDoubleLittleEndian(destination[24..], encodedTime);
        if (detection is not { } value)
        {
            return;
        }

        var section = destination[BaseHeaderLength..];
        BinaryPrimitives.WriteUInt32LittleEndian(section, (uint)value.Sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(section[4..], (ushort)value.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(section[6..], 0);
        var offset = FaceSectionLength;
        foreach (var face in faces[..value.Count])
        {
            BinaryPrimitives.WriteSingleLittleEndian(section[offset..], face.Left);
            BinaryPrimitives.WriteSingleLittleEndian(section[(offset + 4)..], face.Top);
            BinaryPrimitives.WriteSingleLittleEndian(section[(offset + 8)..], face.Right);
            BinaryPrimitives.WriteSingleLittleEndian(section[(offset + 12)..], face.Bottom);
            BinaryPrimitives.WriteSingleLittleEndian(section[(offset + 16)..], face.Confidence);
            offset += FaceLength;
        }
    }

    //--------------------------------------------------------------------------------
    // MJPEG
    //--------------------------------------------------------------------------------

    public const string Boundary = "frame";

    public const string MjpegContentType = "multipart/x-mixed-replace; boundary=" + Boundary;

    private static readonly byte[] PartHeaderPrefix = Encoding.ASCII.GetBytes($"--{Boundary}\r\nContent-Type: image/jpeg\r\nContent-Length: ");

    public static ReadOnlySpan<byte> PartTrailer => "\r\n"u8;

    public static int WritePartHeader(Span<byte> destination, int length)
    {
        PartHeaderPrefix.CopyTo(destination);
        var written = PartHeaderPrefix.Length;
        if (!length.TryFormat(destination[written..], out var digits, default, CultureInfo.InvariantCulture))
        {
            throw new ArgumentException("Destination is too small.", nameof(destination));
        }

        written += digits;
        "\r\n\r\n"u8.CopyTo(destination[written..]);
        return written + 4;
    }
}
