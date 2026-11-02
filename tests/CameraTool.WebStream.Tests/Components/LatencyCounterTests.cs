namespace CameraTool.WebStream.Components;

public sealed class LatencyCounterTests
{
    [Fact]
    public void TakeReturnsAverageAndMaxThenResets()
    {
        // Arrange
        var counter = new LatencyCounter();
        counter.Add(TimeSpan.FromMilliseconds(1));
        counter.Add(TimeSpan.FromMilliseconds(4));
        counter.Add(TimeSpan.FromMilliseconds(1));

        // Act
        var first = counter.Take();
        var second = counter.Take();

        // Assert
        Assert.Equal(TimeSpan.FromMilliseconds(2), first.Average);
        Assert.Equal(TimeSpan.FromMilliseconds(4), first.Max);
        Assert.Equal(TimeSpan.Zero, second.Average);
        Assert.Equal(TimeSpan.Zero, second.Max);
    }
}
