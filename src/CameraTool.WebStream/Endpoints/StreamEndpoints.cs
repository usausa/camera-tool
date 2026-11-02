namespace CameraTool.WebStream.Endpoints;

using System.Buffers;
using System.Net.WebSockets;

using CameraTool.WebStream.Components;

using Microsoft.AspNetCore.Http.Features;

public static class StreamEndpoints
{
    private const string WebSocketClient = "WebSocket";

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
        app.MapGet("/stream.mjpg", HandleMjpegAsync);
        app.MapGet("/snapshot.jpg", HandleSnapshotAsync);
    }

    //--------------------------------------------------------------------------------
    // WebSocket
    //--------------------------------------------------------------------------------

    private static async Task HandleWebSocketAsync(HttpContext context, FrameHub hub, IHostApplicationLifetime lifetime, ILoggerFactory loggerFactory)
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
        log.InfoClientConnected(WebSocketClient, connection.RemoteIpAddress, connection.RemotePort);

        var receiveTask = ReceiveAsync();
        try
        {
            await SendAsync(socket, subscription, stopping.Token);

            var status = lifetime.ApplicationStopping.IsCancellationRequested ? WebSocketCloseStatus.EndpointUnavailable : WebSocketCloseStatus.NormalClosure;
            await CloseOutputAsync(socket, status);
        }
        finally
        {
            // Wait for the close from the client, abort if it does not answer
            closing.CancelAfter(CloseTimeout);
            await receiveTask;
        }

        log.InfoClientDisconnected(WebSocketClient, connection.RemoteIpAddress, connection.RemotePort, subscription.SentFrames, subscription.SkippedFrames);

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

    private static async Task SendAsync(WebSocket socket, FrameSubscription subscription, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var frame = await subscription.WaitAsync();
                try
                {
                    await socket.SendAsync(frame.Message, WebSocketMessageType.Binary, true, cancellationToken);
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

    private static async Task HandleMjpegAsync(HttpContext context, FrameHub hub, IHostApplicationLifetime lifetime, ILoggerFactory loggerFactory)
    {
        var log = loggerFactory.CreateLogger(typeof(StreamEndpoints));
        var connection = context.Connection;

        var response = context.Response;
        response.ContentType = StreamProtocol.MjpegContentType;
        response.Headers.CacheControl = NoCache;
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
        using var subscription = hub.Subscribe(stopping.Token);
        log.InfoClientConnected(MjpegClient, connection.RemoteIpAddress, connection.RemotePort);

        var writer = response.BodyWriter;
        try
        {
            while (true)
            {
                var frame = await subscription.WaitAsync();
                try
                {
                    // Copied to the response buffer
                    writer.Write(frame.PartHeader.Span);
                    writer.Write(frame.Jpeg.Span);
                    writer.Write(StreamProtocol.PartTrailer);
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

    private static async Task HandleSnapshotAsync(HttpContext context, FrameHub hub, IHostApplicationLifetime lifetime)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
        stopping.CancelAfter(SnapshotTimeout);
        using var subscription = hub.Subscribe(stopping.Token);

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
            response.ContentLength = frame.Jpeg.Length;
            response.Headers.CacheControl = NoCache;
            await response.Body.WriteAsync(frame.Jpeg, context.RequestAborted);
        }
        finally
        {
            frame.Release();
        }
    }
}
