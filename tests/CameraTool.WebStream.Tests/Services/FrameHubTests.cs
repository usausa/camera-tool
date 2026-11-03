namespace CameraTool.WebStream.Services;

public sealed class FrameHubTests
{
    private readonly FrameHub hub = new();

    private readonly FramePool pool = new();

    //--------------------------------------------------------------------------------
    // Delivery
    //--------------------------------------------------------------------------------

    [Fact]
    public async Task NewSubscriptionWaitsForNextFrame()
    {
        // Arrange
        Publish(1);
        using var subscription = hub.Subscribe(TestContext.Current.CancellationToken);

        // Act
        var wait = subscription.WaitAsync().AsTask();
        var completedBefore = wait.IsCompleted;
        var published = Publish(2);
        var frame = await wait.ConfigureAwait(true);

        // Assert
        Assert.False(completedBefore);
        Assert.Same(published, frame);
        frame.Release();
    }

    [Fact]
    public async Task PublishDeliversToWaitingSubscriptions()
    {
        // Arrange
        using var first = hub.Subscribe(TestContext.Current.CancellationToken);
        using var second = hub.Subscribe(TestContext.Current.CancellationToken);
        var firstWait = first.WaitAsync().AsTask();
        var secondWait = second.WaitAsync().AsTask();

        // Act
        var published = Publish(1);
        var firstFrame = await firstWait.ConfigureAwait(true);
        var secondFrame = await secondWait.ConfigureAwait(true);

        // Assert
        Assert.Equal(2, hub.ClientCount);
        Assert.Same(published, firstFrame);
        Assert.Same(published, secondFrame);
        firstFrame.Release();
        secondFrame.Release();
    }

    [Fact]
    public async Task WaitReturnsLatestFrameOnly()
    {
        // Arrange
        using var subscription = hub.Subscribe(TestContext.Current.CancellationToken);
        Publish(1);
        Publish(2);
        var latest = Publish(3);

        // Act
        var frame = await subscription.WaitAsync().ConfigureAwait(true);

        // Assert
        Assert.Same(latest, frame);
        frame.Release();
    }

    //--------------------------------------------------------------------------------
    // Statistics
    //--------------------------------------------------------------------------------

    [Fact]
    public async Task CompleteCountsSentAndSkippedFrames()
    {
        // Arrange
        using var subscription = hub.Subscribe(TestContext.Current.CancellationToken);
        Publish(1);
        var first = await subscription.WaitAsync().ConfigureAwait(true);
        subscription.Complete(first);
        first.Release();

        // Act (published while the first frame was sent)
        Publish(2);
        Publish(3);
        Publish(4);
        var next = await subscription.WaitAsync().ConfigureAwait(true);
        subscription.Complete(next);
        next.Release();

        // Assert
        Assert.Equal(4, subscription.LastIndex);
        Assert.Equal(2, subscription.SentFrames);
        Assert.Equal(2, subscription.SkippedFrames);
        Assert.Equal(2, hub.SentFrames);
        Assert.Equal(2, hub.SkippedFrames);
    }

    [Fact]
    public async Task SkipCountsFrameAsSkipped()
    {
        // Arrange
        using var subscription = hub.Subscribe(TestContext.Current.CancellationToken);
        Publish(1);
        Publish(2);
        var frame = await subscription.WaitAsync().ConfigureAwait(true);

        // Act (an H.264 frame while waiting for a key frame)
        subscription.Skip(frame);
        frame.Release();

        // Assert (frame 1 was not taken, frame 2 was skipped)
        Assert.Equal(2, subscription.LastIndex);
        Assert.Equal(0, subscription.SentFrames);
        Assert.Equal(2, subscription.SkippedFrames);
        Assert.Equal(2, hub.SkippedFrames);
    }

    //--------------------------------------------------------------------------------
    // Pool
    //--------------------------------------------------------------------------------

    [Fact]
    public async Task FrameReturnsToPoolWhenAllReleased()
    {
        // Arrange (the subscription holds the first frame after the hub has replaced it)
        using var subscription = hub.Subscribe(TestContext.Current.CancellationToken);
        var first = Publish(1);
        var received = await subscription.WaitAsync().ConfigureAwait(true);
        Publish(2);

        // Act
        var whileHeld = pool.Rent();
        var created = pool.Created;
        received.Release();
        var afterRelease = pool.Rent();

        // Assert
        Assert.NotSame(first, whileHeld);
        Assert.Same(first, afterRelease);
        Assert.Equal(created, pool.Created);
        whileHeld.Release();
        afterRelease.Release();
    }

    //--------------------------------------------------------------------------------
    // Cancel and dispose
    //--------------------------------------------------------------------------------

    [Fact]
    public async Task CancelEndsWait()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var subscription = hub.Subscribe(cancellation.Token);
        var wait = subscription.WaitAsync().AsTask();

        // Act
        await cancellation.CancelAsync().ConfigureAwait(true);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait).ConfigureAwait(true);
    }

    [Fact]
    public async Task CanceledSubscriptionFailsAtOnce()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(true);
        using var subscription = hub.Subscribe(cancellation.Token);
        Publish(1);

        // Act
        var wait = subscription.WaitAsync().AsTask();

        // Assert
        Assert.True(wait.IsCanceled);
    }

    [Fact]
    public void DisposeUnsubscribes()
    {
        // Arrange
        using var subscription = hub.Subscribe(TestContext.Current.CancellationToken);
        var subscribed = hub.ClientCount;

        // Act (disposed again at the end)
        subscription.Dispose();

        // Assert
        Assert.Equal(1, subscribed);
        Assert.Equal(0, hub.ClientCount);
        Assert.False(hub.HasClients);
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    // The hub takes over the reference, the frame is returned for comparison
    private EncodedFrame Publish(long index)
    {
        var frame = pool.Rent();
        frame.GetDataBuffer(1)[0] = (byte)index;
        frame.Commit(index, 0, 1, false);
        hub.Publish(frame);
        return frame;
    }
}
