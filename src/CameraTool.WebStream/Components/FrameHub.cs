namespace CameraTool.WebStream.Components;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks.Sources;

using CameraTool.Vision;

// Shares the latest encoded frame with all clients (latest only, no queue)
public sealed class FrameHub
{
    private readonly Lock sync = new();

    private readonly LatencyCounter sendStartLatency = new();

    private readonly LatencyCounter sentLatency = new();

    private FrameSubscription[] subscriptions = [];

    private EncodedFrame? latest;

    private long sentFrames;

    private long skippedFrames;

    public int ClientCount => Volatile.Read(ref subscriptions).Length;

    public bool HasClients => ClientCount > 0;

    public long SentFrames => Interlocked.Read(ref sentFrames);

    public long SkippedFrames => Interlocked.Read(ref skippedFrames);

    // A new subscription waits for a frame newer than the current one
    public FrameSubscription Subscribe(CancellationToken cancellationToken)
    {
        lock (sync)
        {
            var subscription = new FrameSubscription(this, latest?.Index ?? 0, cancellationToken);
            subscriptions = [.. subscriptions, subscription];
            return subscription;
        }
    }

    internal void Unsubscribe(FrameSubscription subscription)
    {
        lock (sync)
        {
            subscriptions = Array.FindAll(subscriptions, x => x != subscription);
        }
    }

    // Takes over the reference of the caller
    public void Publish(EncodedFrame frame)
    {
        EncodedFrame? previous;
        lock (sync)
        {
            previous = latest;
            latest = frame;
            foreach (var subscription in subscriptions)
            {
                if (subscription.IsWaiting)
                {
                    frame.AddReference();
                    subscription.Deliver(frame);
                }
            }
        }

        previous?.Release();
    }

    // Returns a newer frame with a reference, or registers the subscription as waiting
    internal bool TryAcquireOrWait(FrameSubscription subscription, [NotNullWhen(true)] out EncodedFrame? frame)
    {
        lock (sync)
        {
            if ((latest is not null) && (latest.Index > subscription.LastIndex))
            {
                latest.AddReference();
                frame = latest;
                return true;
            }

            subscription.BeginWait();
            frame = null;
            return false;
        }
    }

    internal void Cancel(FrameSubscription subscription, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (subscription.IsWaiting)
            {
                subscription.Fail(new OperationCanceledException(cancellationToken));
            }
        }
    }

    internal void AddSendStart(long publishTimestamp) => sendStartLatency.Add(Stopwatch.GetElapsedTime(publishTimestamp));

    internal void AddSent(long skipped, long publishTimestamp)
    {
        Interlocked.Increment(ref sentFrames);
        Interlocked.Add(ref skippedFrames, skipped);
        sentLatency.Add(Stopwatch.GetElapsedTime(publishTimestamp));
    }

    internal void AddSkipped(long skipped) => Interlocked.Add(ref skippedFrames, skipped);

    //--------------------------------------------------------------------------------
    // Statistics
    //--------------------------------------------------------------------------------

    // Published -> the send of a client starts
    public LatencySnapshot TakeSendStartLatency() => sendStartLatency.Take();

    // Published -> sent to a client (written to the response for MJPEG)
    public LatencySnapshot TakeSentLatency() => sentLatency.Take();
}

// Used by one connection, waits without allocation
public sealed class FrameSubscription : IValueTaskSource<EncodedFrame>, IDisposable
{
    private readonly FrameHub hub;

    private readonly CancellationToken cancellationToken;

    private readonly CancellationTokenRegistration registration;

    private ManualResetValueTaskSourceCore<EncodedFrame> source = new() { RunContinuationsAsynchronously = true };

    private int disposed;

    // Guarded by the hub lock
    internal bool IsWaiting { get; private set; }

    public long LastIndex { get; private set; }

    public long SentFrames { get; private set; }

    public long SkippedFrames { get; private set; }

    internal FrameSubscription(FrameHub hub, long index, CancellationToken cancellationToken)
    {
        this.hub = hub;
        this.cancellationToken = cancellationToken;
        LastIndex = index;
        registration = cancellationToken.UnsafeRegister(static (state, token) => ((FrameSubscription)state!).OnCanceled(token), this);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            registration.Dispose();
            hub.Unsubscribe(this);
        }
    }

    // Waits for a frame newer than the last sent one, the frame must be released after use (one wait at a time)
    public ValueTask<EncodedFrame> WaitAsync()
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<EncodedFrame>(cancellationToken);
        }

        if (hub.TryAcquireOrWait(this, out var frame))
        {
            hub.AddSendStart(frame.PublishTimestamp);
            return new ValueTask<EncodedFrame>(frame);
        }

        return new ValueTask<EncodedFrame>(this, source.Version);
    }

    public void Complete(EncodedFrame frame)
    {
        // Frames published while sending the previous one are skipped
        var skipped = frame.Index - LastIndex - 1;
        LastIndex = frame.Index;
        SentFrames++;
        SkippedFrames += skipped;
        hub.AddSent(skipped, frame.PublishTimestamp);
    }

    // The frame is not sent (an H.264 frame while the client waits for a key frame)
    public void Skip(EncodedFrame frame)
    {
        var skipped = frame.Index - LastIndex;
        LastIndex = frame.Index;
        SkippedFrames += skipped;
        hub.AddSkipped(skipped);
    }

    private void OnCanceled(CancellationToken token) => hub.Cancel(this, token);

    //--------------------------------------------------------------------------------
    // Called by the hub in the lock
    //--------------------------------------------------------------------------------

    internal void BeginWait()
    {
        source.Reset();
        IsWaiting = true;
    }

    internal void Deliver(EncodedFrame frame)
    {
        IsWaiting = false;
        source.SetResult(frame);
    }

    internal void Fail(Exception exception)
    {
        IsWaiting = false;
        source.SetException(exception);
    }

    //--------------------------------------------------------------------------------
    // IValueTaskSource
    //--------------------------------------------------------------------------------

    EncodedFrame IValueTaskSource<EncodedFrame>.GetResult(short token)
    {
        // Continuation of the waiter
        var frame = source.GetResult(token);
        hub.AddSendStart(frame.PublishTimestamp);
        return frame;
    }

    ValueTaskSourceStatus IValueTaskSource<EncodedFrame>.GetStatus(short token) => source.GetStatus(token);

    void IValueTaskSource<EncodedFrame>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
        source.OnCompleted(continuation, state, token, flags);
}
