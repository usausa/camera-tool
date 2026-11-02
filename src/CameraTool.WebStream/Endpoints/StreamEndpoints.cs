namespace CameraTool.WebStream.Endpoints;

using System.Buffers;
using System.Net.WebSockets;

using CameraTool.WebStream.Components;

using Microsoft.AspNetCore.Http.Features;

public static class StreamEndpoints
{
    private const string WebSocketClient = "WebSocket";

    private const string H264Client = "WebSocket (H.264)";

    private const string MjpegClient = "MJPEG";

    private const string NoCache = "no-cache, no-store";

    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan SnapshotTimeout = TimeSpan.FromSeconds(5);

    //--------------------------------------------------------------------------------
    // Mapping
    //--------------------------------------------------------------------------------

    public static void MapStreamEndpoints(this WebApplication app)
    {
        app.Map("/ws", HandleWebSocketAsync);
        app.Map("/ws-h264", HandleH264WebSocketAsync);
        app.MapGet("/stream.mjpg", HandleMjpegAsync);
        app.MapGet("/snapshot.jpg", HandleSnapshotAsync);
    }

    //--------------------------------------------------------------------------------
    // WebSocket
    //--------------------------------------------------------------------------------

    private static Task HandleWebSocketAsync(HttpContext context, StreamHubs hubs, IHostApplicationLifetime lifetime, ILoggerFactory loggerFactory) =>
        ServeWebSocketAsync(context, WebSocketClient, hubs.Jpeg, null, lifetime, loggerFactory);

    private static Task HandleH264WebSocketAsync(HttpContext context, StreamHubs hubs, H264StreamService h264, IHostApplicationLifetime lifetime, ILoggerFactory loggerFactory)
    {
        if (!h264.IsEnabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return ServeWebSocketAsync(context, H264Client, hubs.H264, h264, lifetime, loggerFactory);
    }

    // h264: the stream of H.264 (null for JPEG)
    private static async Task ServeWebSocketAsync(HttpContext context, string type, FrameHub hub, H264StreamService? h264, IHostApplicationLifetime lifetime, ILoggerFactory loggerFactory)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var log = loggerFactory.CreateLogger(typeof(StreamEndpoints));
        var connection = context.Connection;

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
        using var subscription = hub.Subscribe(stopping.Token);
        using var closing = new CancellationTokenSource();
        log.InfoClientConnected(type, connection.RemoteIpAddress, connection.RemotePort);

        var receiveTask = ReceiveAsync();
        try
        {
            await SendAsync(socket, subscription, h264, stopping.Token);

            var status = lifetime.ApplicationStopping.IsCancellationRequested ? WebSocketCloseStatus.EndpointUnavailable : WebSocketCloseStatus.NormalClosure;
            await CloseOutputAsync(socket, status);
        }
        finally
        {
            // Wait for the close from the client, abort if it does not answer
            closing.CancelAfter(CloseTimeout);
            await receiveTask;
        }

        log.InfoClientDisconnected(type, connection.RemoteIpAddress, connection.RemotePort, subscription.SentFrames, subscription.SkippedFrames);

        // Receives until the client closes, then stops sending
        async Task ReceiveAsync()
        {
            var buffer = new byte[256];
            try
            {
                while (true)
                {
                    var result = await socket.ReceiveAsync(buffer.AsMemory(), closing.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    // A text message from an H.264 client that cannot decode (a decoder error or a full queue) asks for a key frame
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        h264?.RequestKeyFrame();
                    }
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                // Connection lost or aborted
            }
            finally
            {
                await stopping.CancelAsync();
            }
        }
    }

    // H.264 frames wait for a key frame (requested from the encoder) at the start and after a skipped frame
    private static async Task SendAsync(WebSocket socket, FrameSubscription subscription, H264StreamService? h264, CancellationToken cancellationToken)
    {
        var keyFrameGate = h264 is not null ? new KeyFrameGate() : null;
        using var sender = new FrameSender(socket, cancellationToken);
        h264?.RequestKeyFrame();
        try
        {
            while (true)
            {
                var frame = await subscription.WaitAsync();
                try
                {
                    if ((keyFrameGate is not null) && !keyFrameGate.CanSend(frame.Index, frame.IsKeyFrame, subscription.LastIndex))
                    {
                        subscription.Skip(frame);
                        h264?.RequestKeyFrame();
                        continue;
                    }

                    await sender.SendAsync(frame.Message);
                    subscription.Complete(frame);
                }
                finally
                {
                    frame.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Closed by the client or the application is stopping
        }
        catch (WebSocketException)
        {
            // Connection lost
        }
    }

    private static async Task CloseOutputAsync(WebSocket socket, WebSocketCloseStatus status)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        using var timeout = new CancellationTokenSource(CloseTimeout);
        try
        {
            await socket.CloseOutputAsync(status, null, timeout.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // Connection lost
        }
    }

    //--------------------------------------------------------------------------------
    // MJPEG
    //--------------------------------------------------------------------------------

    private static async Task HandleMjpegAsync(HttpContext context, StreamHubs hubs, IHostApplicationLifetime lifetime, ILoggerFactory loggerFactory)
    {
        var log = loggerFactory.CreateLogger(typeof(StreamEndpoints));
        var connection = context.Connection;

        var response = context.Response;
        response.ContentType = StreamProtocol.MjpegContentType;
        response.Headers.CacheControl = NoCache;
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
        using var subscription = hubs.Jpeg.Subscribe(stopping.Token);
        log.InfoClientConnected(MjpegClient, connection.RemoteIpAddress, connection.RemotePort);

        var writer = response.BodyWriter;
        writer.Write(StreamProtocol.FirstPartHeader);
        try
        {
            while (true)
            {
                var frame = await subscription.WaitAsync();
                try
                {
                    // Copied to the response buffer
                    writer.Write(frame.Data.Span);
                    writer.Write(StreamProtocol.NextPartHeader);
                    subscription.Complete(frame);
                }
                finally
                {
                    frame.Release();
                }

                var result = await writer.FlushAsync(stopping.Token);
                if (result.IsCompleted || result.IsCanceled)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            // Closed by the client or the application is stopping
        }

        log.InfoClientDisconnected(MjpegClient, connection.RemoteIpAddress, connection.RemotePort, subscription.SentFrames, subscription.SkippedFrames);
    }

    //--------------------------------------------------------------------------------
    // Snapshot
    //--------------------------------------------------------------------------------

    private static async Task HandleSnapshotAsync(HttpContext context, StreamHubs hubs, IHostApplicationLifetime lifetime)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
        stopping.CancelAfter(SnapshotTimeout);
        using var subscription = hubs.Jpeg.Subscribe(stopping.Token);

        EncodedFrame frame;
        try
        {
            frame = await subscription.WaitAsync();
        }
        catch (OperationCanceledException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        try
        {
            var response = context.Response;
            response.ContentType = "image/jpeg";
            response.ContentLength = frame.Data.Length;
            response.Headers.CacheControl = NoCache;
            await response.Body.WriteAsync(frame.Data, context.RequestAborted);
        }
        finally
        {
            frame.Release();
        }
    }

    // Sends a WebSocket frame without the token: with a cancelable token, ManagedWebSocket takes its slower path for each send
    // (an asynchronous wait and a registration, and the send buffer goes back to the pool on another thread, which allocates).
    // A send in progress when the token is canceled is stopped by aborting the connection, as ManagedWebSocket does.
    private sealed class FrameSender : IDisposable
    {
        private readonly WebSocket socket;

        private readonly CancellationToken cancellationToken;

        private readonly CancellationTokenRegistration registration;

        private int sending;

        public FrameSender(WebSocket socket, CancellationToken cancellationToken)
        {
            this.socket = socket;
            this.cancellationToken = cancellationToken;
            registration = cancellationToken.UnsafeRegister(static state => ((FrameSender)state!).OnCanceled(), this);
        }

        public void Dispose() => registration.Dispose();

        public async ValueTask SendAsync(ReadOnlyMemory<byte> message)
        {
            // Full fence: either the cancel sees the send, or the send sees the cancel
            Interlocked.Exchange(ref sending, 1);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await socket.SendAsync(message, WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            finally
            {
                Volatile.Write(ref sending, 0);
            }
        }

        // Not while waiting for a frame, so that the connection is closed normally
        private void OnCanceled()
        {
            if (Volatile.Read(ref sending) == 1)
            {
                socket.Abort();
            }
        }
    }
}
