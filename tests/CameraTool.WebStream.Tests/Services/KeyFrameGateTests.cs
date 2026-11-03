namespace CameraTool.WebStream.Services;

public sealed class KeyFrameGateTests
{
    [Fact]
    public void NewClientStartsAtKeyFrame()
    {
        // Arrange (subscribed after frame 10)
        var gate = new KeyFrameGate();

        // Assert
        Assert.False(gate.CanSend(11, false, 10));
        Assert.False(gate.CanSend(12, false, 11));
        Assert.True(gate.CanSend(13, true, 12));
        Assert.True(gate.CanSend(14, false, 13));
    }

    [Fact]
    public void WaitsForKeyFrameAfterGap()
    {
        // Arrange
        var gate = new KeyFrameGate();
        Assert.True(gate.CanSend(1, true, 0));
        Assert.True(gate.CanSend(2, false, 1));

        // Assert (frames 3 and 4 were published while frame 2 was sent)
        Assert.False(gate.CanSend(5, false, 2));
        Assert.False(gate.CanSend(6, false, 5));
        Assert.True(gate.CanSend(7, true, 6));
        Assert.True(gate.CanSend(8, false, 7));
    }

    [Fact]
    public void SendsKeyFrameAfterGap()
    {
        // Arrange
        var gate = new KeyFrameGate();
        Assert.True(gate.CanSend(1, true, 0));

        // Assert
        Assert.True(gate.CanSend(4, true, 1));
        Assert.True(gate.CanSend(5, false, 4));
    }
}
