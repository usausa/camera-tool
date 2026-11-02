namespace CameraTool.Vision;

public sealed class ObjectDetectorTests
{
    private const double Tolerance = 1e-6;

    //--------------------------------------------------------------------------------
    // Decode
    //--------------------------------------------------------------------------------

    [Fact]
    public void DecodeTakesBestTargetClassAboveThreshold()
    {
        // Arrange (4 classes, class 1 is not a target)
        float[] logits =
        [
            2f, 5f, 1f, -3f,
            -1f, -2f, -0.5f, -3f,
            0.1f, 0f, 3f, 0f
        ];
        float[] boxes =
        [
            0.5f, 0.5f, 0.25f, 0.5f,
            0.5f, 0.5f, 0.1f, 0.1f,
            0.0625f, 0.9375f, 0.25f, 0.25f
        ];
        bool[] targets = [true, false, true, true];
        var candidates = new ObjectBox[3];

        // Act
        var count = ObjectDetector.Decode(logits, boxes, 4, ObjectDetector.ToLogit(0.5f), targets, candidates);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(0, candidates[0].ClassId);
        Assert.Equal(1 / (1 + Math.Exp(-2)), candidates[0].Confidence, Tolerance);
        Assert.Equal((0.375f, 0.25f, 0.625f, 0.75f), (candidates[0].Left, candidates[0].Top, candidates[0].Right, candidates[0].Bottom));
        Assert.Equal(2, candidates[1].ClassId);
        Assert.Equal(1 / (1 + Math.Exp(-3)), candidates[1].Confidence, Tolerance);
        Assert.Equal((0f, 0.8125f, 0.1875f, 1f), (candidates[1].Left, candidates[1].Top, candidates[1].Right, candidates[1].Bottom));
    }

    [Fact]
    public void DecodeWithoutThresholdKeepsAllQueries()
    {
        // Arrange
        float[] logits = [-9f, -8f, -7f, -6f];
        float[] boxes = [0.5f, 0.5f, 0.1f, 0.1f, 0.5f, 0.5f, 0.1f, 0.1f];
        bool[] targets = [true, true];
        var candidates = new ObjectBox[2];

        // Act
        var count = ObjectDetector.Decode(logits, boxes, 2, ObjectDetector.ToLogit(0f), targets, candidates);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(1, candidates[0].ClassId);
        Assert.Equal(1, candidates[1].ClassId);
    }

    [Fact]
    public void ToLogitIsInverseOfSigmoid()
    {
        Assert.Equal(0f, ObjectDetector.ToLogit(0.5f), Tolerance);
        Assert.Equal(0.7, 1 / (1 + Math.Exp(-ObjectDetector.ToLogit(0.7f))), Tolerance);
        Assert.Equal(Single.PositiveInfinity, ObjectDetector.ToLogit(1f));
    }

    //--------------------------------------------------------------------------------
    // Select
    //--------------------------------------------------------------------------------

    [Fact]
    public void SelectOrdersByConfidenceUpToCapacity()
    {
        // Arrange
        ObjectBox[] candidates =
        [
            new(0f, 0f, 0.1f, 0.1f, 0.6f, 0),
            new(0f, 0f, 0.1f, 0.1f, 0.9f, 2),
            new(0f, 0f, 0.1f, 0.1f, 0.7f, 16)
        ];
        var results = new ObjectBox[2];

        // Act
        var count = ObjectDetector.Select(candidates, results);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(2, results[0].ClassId);
        Assert.Equal(16, results[1].ClassId);
    }

    //--------------------------------------------------------------------------------
    // Label
    //--------------------------------------------------------------------------------

    [Fact]
    public void LabelsAreCocoClasses()
    {
        Assert.Equal(80, ObjectLabels.Count);
        Assert.Equal(0, ObjectLabels.IndexOf("person"));
        Assert.Equal(7, ObjectLabels.IndexOf("TRUCK"));
        Assert.Equal(16, ObjectLabels.IndexOf("dog"));
        Assert.Equal(-1, ObjectLabels.IndexOf("unknown"));
        Assert.Equal("toothbrush", ObjectLabels.Names[79]);
    }

    [Theory]
    [InlineData("person", ObjectGroup.Person)]
    [InlineData("bicycle", ObjectGroup.Vehicle)]
    [InlineData("boat", ObjectGroup.Vehicle)]
    [InlineData("bird", ObjectGroup.Animal)]
    [InlineData("giraffe", ObjectGroup.Animal)]
    [InlineData("traffic light", ObjectGroup.Other)]
    [InlineData("chair", ObjectGroup.Other)]
    public void LabelsHaveGroups(string name, ObjectGroup expected) =>
        Assert.Equal(expected, ObjectLabels.GetGroup(ObjectLabels.IndexOf(name)));

    //--------------------------------------------------------------------------------
    // Model
    //--------------------------------------------------------------------------------

    [Fact]
    public void DetectRunsModel()
    {
        // Arrange
        using var detector = new ObjectDetector(Path.Combine(AppContext.BaseDirectory, "dfine-nano-coco.onnx"), new ObjectDetectorOptions());
        detector.Input.Fill(0.5f);
        var results = new ObjectBox[32];

        // Act
        var count = detector.Detect(results);

        // Assert
        Assert.Equal(640, detector.InputWidth);
        Assert.Equal(640, detector.InputHeight);
        Assert.InRange(count, 0, results.Length);
        Assert.All(results[..count], x =>
        {
            Assert.InRange(x.ClassId, 0, ObjectLabels.Count - 1);
            Assert.InRange(x.Confidence, 0.5f, 1f);
            Assert.InRange(x.Left, 0f, x.Right);
            Assert.InRange(x.Top, 0f, x.Bottom);
        });
    }

    [Fact]
    public void ThrowsWhenClassIsInvalid() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectDetector(Path.Combine(AppContext.BaseDirectory, "dfine-nano-coco.onnx"), new ObjectDetectorOptions { Classes = [80] }));
}
