namespace CameraTool.Vision;

using System.Diagnostics;

public sealed class DetectionWorkerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    // For a condition that must not become true
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(200);

    private static readonly FaceBox LeftBox = new(0.1f, 0.1f, 0.3f, 0.3f, 0.9f);

    private static readonly FaceBox RightBox = new(0.6f, 0.6f, 0.8f, 0.8f, 0.8f);

    //--------------------------------------------------------------------------------
    // Detection
    //--------------------------------------------------------------------------------

    [Fact]
    public void DetectsPreparedInput()
    {
        // Arrange
        using var detector = new FakeDetector(LeftBox, RightBox);
        using var worker = CreateWorker(detector, TimeSpan.Zero);
        using var detected = new SemaphoreSlim(0);
        worker.Detected += (_, _) => detected.Release();
        worker.IsEnabled = true;
        worker.Start();
        Assert.True(SpinWait.SpinUntil(() => worker.IsAvailable, Timeout));

        // Act
        var prepared = worker.TryBeginPrepare();
        worker.EndPrepare(true, 5, 640, 480);
        Assert.True(detected.Wait(Timeout, TestContext.Current.CancellationToken));
        var results = new FaceBox[4];
        var info = worker.CopyResults(results);

        // Assert
        Assert.Same(detector, prepared);
        Assert.Equal(new DetectionInfo(5, 640, 480, 2), info);
        Assert.Equal([LeftBox, RightBox], results[..2]);
        Assert.Equal(2, worker.Count);
        Assert.Equal(1, worker.DetectedFrames);
        Assert.True(worker.TakeInferenceTime().Max >= TimeSpan.Zero);
    }

    [Fact]
    public void RemovesBoxesOutsideArea()
    {
        // Arrange (the right half is outside)
        using var detector = new FakeDetector(LeftBox, RightBox);
        using var worker = CreateWorker(detector, TimeSpan.Zero, DetectionArea.Full.WithCells(16, 0, DetectionArea.Columns - 1, DetectionArea.Rows - 1, false));
        using var detected = new SemaphoreSlim(0);
        worker.Detected += (_, _) => detected.Release();
        worker.IsEnabled = true;
        worker.Start();
        Assert.True(SpinWait.SpinUntil(() => worker.IsAvailable, Timeout));

        // Act
        worker.TryBeginPrepare();
        worker.EndPrepare(true, 1, 640, 480);
        Assert.True(detected.Wait(Timeout, TestContext.Current.CancellationToken));
        var results = new FaceBox[4];
        var info = worker.CopyResults(results);

        // Assert
        Assert.Equal(1, info.Count);
        Assert.Equal(LeftBox, results[0]);
    }

    [Fact]
    public void DoesNotPrepareWhileDetecting()
    {
        // Arrange (the inference waits for the release)
        using var detector = new FakeDetector(LeftBox) { IsBlocking = true };
        using var worker = CreateWorker(detector, TimeSpan.Zero);
        using var detected = new SemaphoreSlim(0);
        worker.Detected += (_, _) => detected.Release();
        worker.IsEnabled = true;
        worker.Start();
        Assert.True(SpinWait.SpinUntil(() => worker.IsAvailable, Timeout));
        worker.TryBeginPrepare();
        worker.EndPrepare(true, 1, 640, 480);

        // Act
        var whileDetecting = worker.TryBeginPrepare();
        detector.Unblock();
        Assert.True(detected.Wait(Timeout, TestContext.Current.CancellationToken));
        var afterDetection = worker.TryBeginPrepare();
        worker.EndPrepare(false, 2, 640, 480);

        // Assert
        Assert.Null(whileDetecting);
        Assert.Same(detector, afterDetection);
    }

    [Fact]
    public void InputNotPreparedReturnsToIdle()
    {
        // Arrange
        using var detector = new FakeDetector(LeftBox);
        using var worker = CreateWorker(detector, TimeSpan.Zero);
        worker.IsEnabled = true;
        worker.Start();
        Assert.True(SpinWait.SpinUntil(() => worker.IsAvailable, Timeout));

        // Act
        worker.TryBeginPrepare();
        worker.EndPrepare(false, 1, 640, 480);
        var again = worker.TryBeginPrepare();
        worker.EndPrepare(false, 2, 640, 480);

        // Assert (no inference ran)
        Assert.Same(detector, again);
        Assert.False(SpinWait.SpinUntil(() => worker.DetectedFrames > 0, Settle));
    }

    [Fact]
    public void WaitsForInterval()
    {
        // Arrange
        var interval = TimeSpan.FromMilliseconds(300);
        using var detector = new FakeDetector(LeftBox);
        using var worker = CreateWorker(detector, interval);
        using var detected = new SemaphoreSlim(0);
        worker.Detected += (_, _) => detected.Release();
        worker.IsEnabled = true;
        worker.Start();
        Assert.True(SpinWait.SpinUntil(() => worker.IsAvailable, Timeout));
        var start = Stopwatch.GetTimestamp();
        worker.TryBeginPrepare();
        worker.EndPrepare(true, 1, 640, 480);
        Assert.True(detected.Wait(Timeout, TestContext.Current.CancellationToken));

        // Act
        var withinInterval = worker.TryBeginPrepare();
        IDetector<FaceBox>? afterInterval = null;
        Assert.True(SpinWait.SpinUntil(() => (afterInterval = worker.TryBeginPrepare()) is not null, Timeout));
        var elapsed = Stopwatch.GetElapsedTime(start);
        worker.EndPrepare(false, 2, 640, 480);

        // Assert
        Assert.Null(withinInterval);
        Assert.Same(detector, afterInterval);
        Assert.True(elapsed >= interval, $"elapsed={elapsed.TotalMilliseconds:F0}ms");
    }

    [Fact]
    public void SkipsInputWhileCallerSkips()
    {
        // Arrange (as the motion gate while there is no motion)
        using var detector = new FakeDetector(LeftBox);
        var skipping = true;
        var timestamps = new List<long>();
        using var worker = CreateWorker(detector, TimeSpan.Zero, skip: x =>
        {
            timestamps.Add(x);
            return skipping;
        });
        using var detected = new SemaphoreSlim(0);
        worker.Detected += (_, _) => detected.Release();
        worker.IsEnabled = true;
        worker.Start();
        Assert.True(SpinWait.SpinUntil(() => worker.IsAvailable, Timeout));

        // Act
        var whileSkipping = worker.TryBeginPrepare();
        skipping = false;
        var start = Stopwatch.GetTimestamp();
        var afterSkipping = worker.TryBeginPrepare();
        worker.EndPrepare(true, 1, 640, 480);
        Assert.True(detected.Wait(Timeout, TestContext.Current.CancellationToken));
        skipping = true;
        var afterDetection = worker.TryBeginPrepare();

        // Assert (the caller is given the time of the last prepared input)
        Assert.Null(whileSkipping);
        Assert.Same(detector, afterSkipping);
        Assert.Null(afterDetection);
        Assert.Equal(3, timestamps.Count);
        Assert.Equal(0, timestamps[0]);
        Assert.Equal(0, timestamps[1]);
        Assert.True(timestamps[2] >= start);
    }

    [Fact]
    public void ClearedResultsAreNotPublished()
    {
        // Arrange (the inference waits for the release)
        using var detector = new FakeDetector(LeftBox) { IsBlocking = true };
        using var worker = CreateWorker(detector, TimeSpan.Zero);
        var events = 0;
        worker.Detected += (_, _) => Interlocked.Increment(ref events);
        worker.IsEnabled = true;
        worker.Start();
        Assert.True(SpinWait.SpinUntil(() => worker.IsAvailable, Timeout));
        worker.TryBeginPrepare();
        worker.EndPrepare(true, 1, 640, 480);

        // Act (cleared during the inference, as when the camera stops)
        worker.Clear();
        detector.Unblock();
        Assert.True(SpinWait.SpinUntil(() => worker.DetectedFrames == 1, Timeout));
        var info = worker.CopyResults(new FaceBox[4]);

        // Assert (only the clear raised the event)
        Assert.Equal(default, info);
        Assert.False(SpinWait.SpinUntil(() => Volatile.Read(ref events) > 1, Settle));
    }

    //--------------------------------------------------------------------------------
    // Load
    //--------------------------------------------------------------------------------

    [Fact]
    public void LoadsOnlyWhenEnabled()
    {
        // Arrange
        using var detector = new FakeDetector(LeftBox);
        var loads = 0;
        using var worker = new DetectionWorker<FaceBox>("Test", 4, TimeSpan.Zero, static _ => false, () =>
        {
            Interlocked.Increment(ref loads);
            return detector;
        }, static () => DetectionArea.Full);
        worker.Start();

        // Act
        var loadedWhileDisabled = SpinWait.SpinUntil(() => Volatile.Read(ref loads) > 0, Settle);
        var preparedWhileDisabled = worker.TryBeginPrepare();
        worker.IsEnabled = true;
        Assert.True(SpinWait.SpinUntil(() => worker.IsAvailable, Timeout));
        worker.IsEnabled = false;
        worker.IsEnabled = true;

        // Assert
        Assert.False(loadedWhileDisabled);
        Assert.Null(preparedWhileDisabled);
        Assert.False(SpinWait.SpinUntil(() => Volatile.Read(ref loads) > 1, Settle));
    }

    [Fact]
    public void LoadFailureIsNotRetried()
    {
        // Arrange (the load fails)
        var loads = 0;
        using var worker = new DetectionWorker<FaceBox>("Test", 4, TimeSpan.Zero, static _ => false, () =>
        {
            Interlocked.Increment(ref loads);
            return null;
        }, static () => DetectionArea.Full);
        worker.IsEnabled = true;
        worker.Start();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref loads) == 1, Timeout));

        // Act
        worker.IsEnabled = false;
        worker.IsEnabled = true;

        // Assert
        Assert.False(SpinWait.SpinUntil(() => Volatile.Read(ref loads) > 1, Settle));
        Assert.False(worker.IsAvailable);
        Assert.Null(worker.TryBeginPrepare());
    }

    [Fact]
    public void DisposeReleasesDetector()
    {
        // Arrange
        using var detector = new FakeDetector(LeftBox);
        using var worker = CreateWorker(detector, TimeSpan.Zero);
        worker.IsEnabled = true;
        worker.Start();
        Assert.True(SpinWait.SpinUntil(() => worker.IsAvailable, Timeout));

        // Act
        worker.Stop();
        var disposedAfterStop = detector.IsDisposed;
        worker.Dispose();

        // Assert
        Assert.False(disposedAfterStop);
        Assert.True(detector.IsDisposed);
    }

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static DetectionWorker<FaceBox> CreateWorker(FakeDetector detector, TimeSpan interval, DetectionArea? area = null, Func<long, bool>? skip = null)
    {
        var current = area ?? DetectionArea.Full;
        return new DetectionWorker<FaceBox>("Test", 4, interval, skip ?? (static _ => false), () => detector, () => current);
    }

    // Returns the same boxes for any input
    private sealed class FakeDetector : IDetector<FaceBox>
    {
        private readonly FaceBox[] boxes;

        private readonly float[] input = new float[3 * 4 * 4];

        private readonly ManualResetEventSlim unblocked = new(false);

        public FakeDetector(params FaceBox[] boxes)
        {
            this.boxes = boxes;
        }

        // Detect waits for Unblock
        public bool IsBlocking { get; init; }

        public bool IsDisposed { get; private set; }

        public int InputWidth => 4;

        public int InputHeight => 4;

        public Span<float> Input => input;

        public void Dispose()
        {
            IsDisposed = true;
            unblocked.Dispose();
        }

        public void Unblock() => unblocked.Set();

        public int Detect(Span<FaceBox> results)
        {
            if (IsBlocking)
            {
                unblocked.Wait(Timeout);
            }

            boxes.CopyTo(results);
            return boxes.Length;
        }
    }
}
