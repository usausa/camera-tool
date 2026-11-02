namespace CameraTool.Vision;

using System.Globalization;

// Annex B access units of H.264, used by the tests to check the encoder output
// (the viewer reads the codec string from the SPS by itself)
internal static class H264Bitstream
{
    private const int IdrSlice = 5;

    private const int SequenceParameterSet = 7;

    // True when the access unit has an IDR slice
    public static bool IsKeyFrame(ReadOnlySpan<byte> data) => FindUnit(data, IdrSlice) >= 0;

    // Codec string for WebCodecs (avc1.PPCCLL: profile_idc, constraint flags, level_idc) from the SPS, null without an SPS
    public static string? GetCodecString(ReadOnlySpan<byte> data)
    {
        var offset = FindUnit(data, SequenceParameterSet);
        return (offset >= 0) && (offset + 3 < data.Length)
            ? String.Create(CultureInfo.InvariantCulture, $"avc1.{data[offset + 1]:X2}{data[offset + 2]:X2}{data[offset + 3]:X2}")
            : null;
    }

    // Offset of the header of the first NAL unit of the type, -1 when not found
    private static int FindUnit(ReadOnlySpan<byte> data, int type)
    {
        for (var i = 0; i + 3 < data.Length; i++)
        {
            if ((data[i] == 0) && (data[i + 1] == 0) && (data[i + 2] == 1))
            {
                if ((data[i + 3] & 0x1F) == type)
                {
                    return i + 3;
                }

                i += 2;
            }
        }

        return -1;
    }
}
