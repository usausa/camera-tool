namespace CameraTool.Vision;

public sealed class H264EncoderTests
{
    private const int Width = 320;

    private const int Height = 240;

    private const int Frames = 40;

    private const int KeyFrameRequest = 20;

    private const long FrameDuration = 333_333;

    private static readonly TimeSpan OutputTimeout = TimeSpan.FromSeconds(5);

    private static readonly byte[] StartCode = [0, 0, 0, 1];

    //--------------------------------------------------------------------------------
    // Encoder
    //--------------------------------------------------------------------------------

    [Fact]
    public void SoftwareEncoderEncodesFrames()
    {
        // Act
        var (outputs, hardware) = Encode(false, EncodeNv12);

        // Assert
        Assert.False(hardware);
        AssertOutputs(outputs);
    }

    [Fact]
    public void HardwareEncoderEncodesFrames()
    {
        // Act
        var (outputs, hardware) = Encode(true, EncodeNv12);

        // Assert
        Assert.SkipUnless(hardware, "Hardware encoder is not available.");
        AssertOutputs(outputs);
    }

    [Fact]
    public void Yuy2IsEncodedLikeConvertedNv12()
    {
        // Act (the software encoder gives the same output for the same input)
        var (direct, _) = Encode(false, static (encoder, index, timestamp) => encoder.EncodeYuy2(CreateYuy2(index), Width * 2, timestamp));
        var (converted, _) = Encode(false, static (encoder, index, timestamp) =>
        {
            var frame = new byte[Width * Height * 3 / 2];
            ImageConverter.Yuy2ToNv12(CreateYuy2(index), Width * 2, frame.AsSpan(0, Width * Height), Width, frame.AsSpan(Width * Height), Width, Width, Height);
            return encoder.Encode(frame.AsSpan(0, Width * Height), Width, frame.AsSpan(Width * Height), Width, timestamp);
        });

        // Assert
        AssertOutputs(direct);
        Assert.Equal(converted.Select(static x => x.Data), direct.Select(static x => x.Data));
    }

    [Fact]
    public void ThrowsWhenFrameSizeIsOdd() =>
        Assert.Throws<ArgumentException>(() => new H264Encoder(new H264EncoderOptions { Width = 321, Height = 240 }, static (_, _, _) => { }));

    [Fact]
    public void EncodeYuy2ThrowsWhenSourceIsShort()
    {
        // Arrange
        using var encoder = new H264Encoder(new H264EncoderOptions { Width = Width, Height = Height, PreferHardware = false }, static (_, _, _) => { });

        // Act and Assert
        Assert.Throws<ArgumentException>(() => encoder.EncodeYuy2(new byte[(Width * 2 * Height) - 1], Width * 2, 0));
    }

    //--------------------------------------------------------------------------------
    // Bitstream
    //--------------------------------------------------------------------------------

    [Fact]
    public void BitstreamReadsKeyFrameAndCodec()
    {
        // Arrange (AUD, SPS (Main, level 3.1), PPS, IDR slice)
        byte[] key = [0, 0, 0, 1, 0x09, 0xF0, 0, 0, 0, 1, 0x67, 0x4D, 0x40, 0x1F, 0xAA, 0, 0, 0, 1, 0x68, 0xEE, 0, 0, 1, 0x65, 0x88, 0x84];
        byte[] delta = [0, 0, 0, 1, 0x09, 0xF0, 0, 0, 1, 0x41, 0x9A, 0x02];

        // Assert
        Assert.True(H264Bitstream.IsKeyFrame(key));
        Assert.Equal("avc1.4D401F", H264Bitstream.GetCodecString(key));
        Assert.False(H264Bitstream.IsKeyFrame(delta));
        Assert.Null(H264Bitstream.GetCodecString(delta));
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    // One frame at a time: the encoders output the frame for each input in the low latency mode.
    // encodeFrame: passes the frame of the index with the timestamp
    private static (List<(byte[] Data, bool Key, long Timestamp)> Outputs, bool Hardware) Encode(bool preferHardware, Func<H264Encoder, int, long, bool> encodeFrame)
    {
        var outputs = new List<(byte[] Data, bool Key, long Timestamp)>();
        using var arrived = new SemaphoreSlim(0);
        bool hardware;
        using (var encoder = new H264Encoder(
            new H264EncoderOptions { Width = Width, Height = Height, Bitrate = 500_000, KeyFrameInterval = 30, PreferHardware = preferHardware },
            (data, key, timestamp) =>
            {
                lock (outputs)
                {
                    outputs.Add((data.ToArray(), key, timestamp));
                }

                arrived.Release();
            }))
        {
            hardware = encoder.IsHardware;
            Assert.NotEmpty(encoder.Name);
            for (var i = 0; i < Frames; i++)
            {
                if (i == KeyFrameRequest)
                {
                    encoder.RequestKeyFrame();
                }

                Assert.True(encodeFrame(encoder, i, (i + 1) * FrameDuration));
                Assert.True(arrived.Wait(OutputTimeout));
            }
        }

        return (outputs, hardware);
    }

    private static void AssertOutputs(List<(byte[] Data, bool Key, long Timestamp)> outputs)
    {
        Assert.Equal(Frames, outputs.Count);
        Assert.Equal(StartCode, outputs[0].Data[..4]);
        Assert.True(outputs[0].Key);
        Assert.True(H264Bitstream.IsKeyFrame(outputs[0].Data));
        Assert.StartsWith("avc1.4D", H264Bitstream.GetCodecString(outputs[0].Data), StringComparison.Ordinal);
        Assert.All(outputs[1..KeyFrameRequest], static x => Assert.False(x.Key));
        Assert.True(outputs[KeyFrameRequest].Key);
        Assert.NotNull(H264Bitstream.GetCodecString(outputs[KeyFrameRequest].Data));
        for (var i = 0; i < Frames; i++)
        {
            Assert.Equal((i + 1) * FrameDuration, outputs[i].Timestamp);
        }
    }

    // NV12 moving gradient
    private static bool EncodeNv12(H264Encoder encoder, int index, long timestamp)
    {
        var frame = new byte[Width * Height * 3 / 2];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                frame[(y * Width) + x] = (byte)((x + y + (index * 4)) & 0xFF);
            }
        }

        frame.AsSpan(Width * Height).Fill(128);
        return encoder.Encode(frame.AsSpan(0, Width * Height), Width, frame.AsSpan(Width * Height), Width, timestamp);
    }

    // YUY2 moving gradient, the chroma differs between rows (averaged by the conversion)
    private static byte[] CreateYuy2(int index)
    {
        var frame = new byte[Width * 2 * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x += 2)
            {
                var offset = (y * Width * 2) + (x * 2);
                frame[offset] = (byte)((x + y + (index * 4)) & 0xFF);
                frame[offset + 1] = (byte)((x + (index * 2)) & 0xFF);
                frame[offset + 2] = (byte)((x + 1 + y + (index * 4)) & 0xFF);
                frame[offset + 3] = (byte)((y + (index * 2)) & 0xFF);
            }
        }

        return frame;
    }
}
