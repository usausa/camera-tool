namespace CameraTool.Vision;

public sealed class FaceDetectorTests
{
    private const double Tolerance = 1e-6;

    //--------------------------------------------------------------------------------
    // Decode
    //--------------------------------------------------------------------------------

    [Fact]
    public void DecodeFiltersByThreshold()
    {
        // Arrange
        float[] scores = [0.9f, 0.1f, 0.2f, 0.8f, 0.4f, 0.6f];
        float[] boxes = [0f, 0f, 0.1f, 0.1f, 0.2f, 0.2f, 0.4f, 0.4f, -0.1f, 0.5f, 0.7f, 1.2f];
        var candidates = new FaceBox[3];

        // Act
        var count = FaceDetector.Decode(scores, boxes, 0.5f, candidates);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(new FaceBox(0.2f, 0.2f, 0.4f, 0.4f, 0.8f), candidates[0]);
        Assert.Equal(new FaceBox(0f, 0.5f, 0.7f, 1f, 0.6f), candidates[1]);
    }

    //--------------------------------------------------------------------------------
    // Suppress
    //--------------------------------------------------------------------------------

    [Fact]
    public void SuppressKeepsHighestOfOverlappingBoxes()
    {
        // Arrange
        FaceBox[] candidates =
        [
            new(0.1f, 0.1f, 0.5f, 0.5f, 0.8f),
            new(0.12f, 0.1f, 0.52f, 0.5f, 0.9f),
            new(0.6f, 0.6f, 0.9f, 0.9f, 0.75f)
        ];
        var results = new FaceBox[8];

        // Act
        var count = FaceDetector.Suppress(candidates, new bool[candidates.Length], 0.3f, results);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(0.9f, results[0].Confidence);
        Assert.Equal(0.75f, results[1].Confidence);
    }

    [Fact]
    public void SuppressStopsAtResultCapacity()
    {
        // Arrange
        FaceBox[] candidates =
        [
            new(0.0f, 0.0f, 0.1f, 0.1f, 0.71f),
            new(0.3f, 0.3f, 0.4f, 0.4f, 0.93f),
            new(0.6f, 0.6f, 0.7f, 0.7f, 0.82f)
        ];
        var results = new FaceBox[2];

        // Act
        var count = FaceDetector.Suppress(candidates, new bool[candidates.Length], 0.3f, results);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(0.93f, results[0].Confidence);
        Assert.Equal(0.82f, results[1].Confidence);
    }

    //--------------------------------------------------------------------------------
    // IoU
    //--------------------------------------------------------------------------------

    [Fact]
    public void CalculateIouOfSameBoxIsOne() =>
        Assert.Equal(1, FaceDetector.CalculateIou(new FaceBox(0.1f, 0.1f, 0.5f, 0.5f, 1f), new FaceBox(0.1f, 0.1f, 0.5f, 0.5f, 1f)), Tolerance);

    [Fact]
    public void CalculateIouOfDisjointBoxesIsZero() =>
        Assert.Equal(0, FaceDetector.CalculateIou(new FaceBox(0f, 0f, 0.2f, 0.2f, 1f), new FaceBox(0.5f, 0.5f, 0.7f, 0.7f, 1f)), Tolerance);

    [Fact]
    public void CalculateIouOfHalfOverlap() =>
        Assert.Equal(1d / 3, FaceDetector.CalculateIou(new FaceBox(0f, 0f, 0.5f, 0.5f, 1f), new FaceBox(0.25f, 0f, 0.75f, 0.5f, 1f)), Tolerance);

    //--------------------------------------------------------------------------------
    // Model
    //--------------------------------------------------------------------------------

    [Fact]
    public void DetectFindsNoFaceInBlankImage()
    {
        // Arrange
        using var detector = new FaceDetector(Path.Combine(AppContext.BaseDirectory, "version-RFB-320.onnx"), new FaceDetectorOptions());
        detector.Input.Clear();
        var results = new FaceBox[8];

        // Act
        var count = detector.Detect(results);

        // Assert
        Assert.Equal(320, detector.InputWidth);
        Assert.Equal(240, detector.InputHeight);
        Assert.Equal(0, count);
    }
}
