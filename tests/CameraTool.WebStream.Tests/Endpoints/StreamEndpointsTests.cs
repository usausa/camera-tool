namespace CameraTool.WebStream.Endpoints;

using System.Buffers.Binary;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;

using CameraTool.WebStream.Components;

public sealed class StreamEndpointsTests
{
    private const uint MotionSectionFlag = 0x2;

    private const uint KeyFrameFlag = 0x8;

    private static readonly byte[] FirstPartHeader = "--frame\r\nContent-Type: image/jpeg\r\n\r\n"u8.ToArray();

    private static readonly byte[] NextPartHeader = "\r\n--frame\r\nContent-Type: image/jpeg\r\n\r\n"u8.ToArray();

    //--------------------------------------------------------------------------------
    // WebSocket
    //--------------------------------------------------------------------------------

    [Fact]
    public Task WebSocketSendsHeaderAndJpeg() =>
        StreamTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Arrange
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(server.GetWebSocketUri("/ws"), cancellationToken).ConfigureAwait(true);

            // Act
            var messages = new List<byte[]>();
            for (var i = 0; i < 5; i++)
            {
                messages.Add(await ReceiveAsync(socket, cancellationToken).ConfigureAwait(true));
            }

            // Assert
            long previous = 0;
            foreach (var message in messages)
            {
                var headerLength = ReadUInt16(message, 2);
                Assert.Equal(StreamProtocol.HeaderVersion, ReadUInt16(message, 0));
                Assert.InRange(headerLength, 32, message.Length - 4);
                Assert.Equal(TestCameraProvider.Width, ReadUInt16(message, 4));
                Assert.Equal(TestCameraProvider.Height, ReadUInt16(message, 6));
                var sequence = ReadUInt32(message, 8);
                Assert.True(sequence > previous);
                previous = sequence;
                // Only the motion section (the face and object detection are off)
                Assert.Equal(MotionSectionFlag, ReadUInt32(message, 12));
                Assert.True(ReadDouble(message, 16) <= ReadDouble(message, 24));
                AssertJpeg(message.AsSpan(headerLength));
            }
        });

    [Fact]
    public Task H264WebSocketStartsAtKeyFrame() =>
        StreamTestServer.RunAsync(
            new Dictionary<string, string?> { ["H264:KeyFrameInterval"] = "60" },
            static async (server, cancellationToken) =>
            {
                // Arrange
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(server.GetWebSocketUri("/ws-h264"), cancellationToken).ConfigureAwait(true);

                // Act (about a second of frames, the key frame interval is 60 seconds)
                var first = await ReceiveAsync(socket, cancellationToken).ConfigureAwait(true);
                var following = new List<byte[]>();
                for (var i = 0; i < 30; i++)
                {
                    following.Add(await ReceiveAsync(socket, cancellationToken).ConfigureAwait(true));
                }

                // Assert
                Assert.True(IsKeyFrame(first));
                AssertAnnexB(first.AsSpan(ReadUInt16(first, 2)));
                var previous = ReadUInt32(first, 8);
                foreach (var message in following)
                {
                    var sequence = ReadUInt32(message, 8);
                    Assert.True(sequence > previous);
                    previous = sequence;
                    Assert.False(IsKeyFrame(message));
                    AssertAnnexB(message.AsSpan(ReadUInt16(message, 2)));
                }
            });

    [Fact]
    public Task H264TextMessageRequestsKeyFrame() =>
        StreamTestServer.RunAsync(
            new Dictionary<string, string?> { ["H264:KeyFrameInterval"] = "60" },
            static async (server, cancellationToken) =>
            {
                // Arrange (the key frame requests are 500 ms apart at least, so the request of the connection has passed)
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(server.GetWebSocketUri("/ws-h264"), cancellationToken).ConfigureAwait(true);
                for (var i = 0; i < 30; i++)
                {
                    await ReceiveAsync(socket, cancellationToken).ConfigureAwait(true);
                }

                // Act
                await socket.SendAsync("key"u8.ToArray().AsMemory(), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(true);
                var received = 0;
                var keyFrame = false;
                while (!keyFrame && (received < 30))
                {
                    keyFrame = IsKeyFrame(await ReceiveAsync(socket, cancellationToken).ConfigureAwait(true));
                    received++;
                }

                // Assert
                Assert.True(keyFrame);
            });

    [Fact]
    public Task H264IsNotFoundWhenDisabled() =>
        StreamTestServer.RunAsync(
            new Dictionary<string, string?> { ["H264:Enable"] = "false" },
            static async (server, cancellationToken) =>
            {
                // Act
                using var response = await server.Client.GetAsync(new Uri("/ws-h264", UriKind.Relative), cancellationToken).ConfigureAwait(true);

                // Assert
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            });

    [Theory]
    [InlineData("/ws")]
    [InlineData("/ws-h264")]
    public Task WebSocketPathRejectsOtherRequest(string path) =>
        StreamTestServer.RunAsync(null, async (server, cancellationToken) =>
        {
            // Act
            using var response = await server.Client.GetAsync(new Uri(path, UriKind.Relative), cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        });

    //--------------------------------------------------------------------------------
    // MJPEG
    //--------------------------------------------------------------------------------

    [Fact]
    public Task MjpegWritesNextPartHeaderAfterJpeg() =>
        StreamTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Act (until the first part ends)
            using var response = await server.Client.GetAsync(new Uri("/stream.mjpg", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true);
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);
            var received = new List<byte>();
            var buffer = new byte[64 * 1024];
            int end;
            while ((end = IndexOf(received, NextPartHeader)) < 0)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(true);
                Assert.NotEqual(0, read);
                received.AddRange(buffer.AsSpan(0, read));
            }

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("multipart/x-mixed-replace; boundary=frame", response.Content.Headers.ContentType?.ToString());
            Assert.True(response.Headers.CacheControl?.NoStore);
            var data = received.ToArray();
            Assert.Equal(FirstPartHeader, data[..FirstPartHeader.Length]);
            AssertJpeg(data.AsSpan(FirstPartHeader.Length, end - FirstPartHeader.Length));
        });

    //--------------------------------------------------------------------------------
    // Snapshot
    //--------------------------------------------------------------------------------

    [Fact]
    public Task SnapshotReturnsJpeg() =>
        StreamTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Act
            using var response = await server.Client.GetAsync(new Uri("/snapshot.jpg", UriKind.Relative), cancellationToken).ConfigureAwait(true);
            var data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal<long?>(data.Length, response.Content.Headers.ContentLength);
            Assert.True(response.Headers.CacheControl?.NoStore);
            AssertJpeg(data);
        });

    [Fact]
    public Task SnapshotIsUnavailableWithoutCamera() =>
        StreamTestServer.RunAsync(
            new Dictionary<string, string?> { ["Camera:Name"] = "Missing camera" },
            static async (server, cancellationToken) =>
            {
                // Act (waits 5 seconds for a frame)
                using var response = await server.Client.GetAsync(new Uri("/snapshot.jpg", UriKind.Relative), cancellationToken).ConfigureAwait(true);

                // Assert
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            });

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static async Task<byte[]> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 1024];
        var length = 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(true);
            Assert.Equal(WebSocketMessageType.Binary, result.MessageType);
            length += result.Count;
            if (result.EndOfMessage)
            {
                return buffer[..length];
            }
        }
    }

    private static bool IsKeyFrame(byte[] message) => (ReadUInt32(message, 12) & KeyFrameFlag) != 0;

    private static void AssertJpeg(ReadOnlySpan<byte> data)
    {
        Assert.True(data.Length > 4);
        Assert.True(data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8]));
        Assert.True(data.EndsWith((ReadOnlySpan<byte>)[0xFF, 0xD9]));
    }

    private static void AssertAnnexB(ReadOnlySpan<byte> data) =>
        Assert.True(data.StartsWith((ReadOnlySpan<byte>)[0, 0, 0, 1]) || data.StartsWith((ReadOnlySpan<byte>)[0, 0, 1]));

    private static int IndexOf(List<byte> data, byte[] value) => CollectionsMarshal.AsSpan(data).IndexOf(value);

    private static int ReadUInt16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));

    private static uint ReadUInt32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static double ReadDouble(byte[] bytes, int offset) => BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(offset));
}
