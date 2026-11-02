namespace CameraTool.WebStream.Endpoints;

using System.Net.WebSockets;
using System.Text.Json;

using CameraTool.WebStream.Components;

public sealed class StatusEndpointsTests
{
    [Fact]
    public Task StatusShowsCapture() =>
        StreamTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Arrange
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(server.GetWebSocketUri("/ws"), cancellationToken).ConfigureAwait(true);

            // Act
            var status = await server.WaitForStatusAsync(
                static x => (x.GetProperty("clients").GetInt32() == 1) && (x.GetProperty("captureFps").GetDouble() > 0),
                cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(TestCameraProvider.DeviceName, status.GetProperty("device").GetString());
            Assert.Equal("YUY2 320x240 30fps", status.GetProperty("format").GetString());
            Assert.True(status.GetProperty("running").GetBoolean());
            Assert.Equal(JsonValueKind.Null, status.GetProperty("error").ValueKind);
            Assert.True(status.GetProperty("h264Enabled").GetBoolean());
        });

    [Fact]
    public Task StatusShowsMissingCamera() =>
        StreamTestServer.RunAsync(
            new Dictionary<string, string?> { ["Camera:Name"] = "Missing camera" },
            static async (server, cancellationToken) =>
            {
                // Act
                var status = await server.WaitForStatusAsync(static x => x.GetProperty("error").ValueKind == JsonValueKind.String, cancellationToken).ConfigureAwait(true);

                // Assert
                Assert.Equal("Camera not found.", status.GetProperty("error").GetString());
                Assert.False(status.GetProperty("running").GetBoolean());
                Assert.Equal(JsonValueKind.Null, status.GetProperty("device").ValueKind);
                Assert.Null(server.Camera.Capture);
            });
}
