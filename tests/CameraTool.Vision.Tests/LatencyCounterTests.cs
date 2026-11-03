namespace CameraTool.Vision;

using System.Globalization;

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

    [Fact]
    public void LatencyValueFormatsAverageAndMax()
    {
        // Arrange
        var value = new LatencyValue(34.06, 48.44);

        // Act
        var text = String.Format(CultureInfo.InvariantCulture, "{0:F1} ms", value);
        var plain = value.ToString("F2", CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal("34.1 / 48.4 ms", text);
        Assert.Equal("34.06 / 48.44", plain);
    }

    [Fact]
    public void LatencyValueTryFormatNeedsTheWholeText()
    {
        // Arrange
        var value = new LatencyValue(1.5, 2.5);
        var exact = new char[11];
        var shorter = new char[10];

        // Act
        var formatted = value.TryFormat(exact, out var exactWritten, "F2", CultureInfo.InvariantCulture);
        var cut = value.TryFormat(shorter, out var shortWritten, "F2", CultureInfo.InvariantCulture);

        // Assert
        Assert.True(formatted);
        Assert.Equal("1.50 / 2.50", new string(exact, 0, exactWritten));
        Assert.False(cut);
        Assert.Equal(0, shortWritten);
    }
}
