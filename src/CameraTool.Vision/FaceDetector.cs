namespace CameraTool.Vision;

using Microsoft.ML.OnnxRuntime;

// Ultra-Light-Fast-Generic-Face-Detector (version-RFB-320)
// Inputs and outputs are bound to pre-allocated buffers, so detection does not allocate
public sealed class FaceDetector : IDetector<FaceBox>
{
    private const string ScoresName = "scores";

    private const string BoxesName = "boxes";

    private readonly InferenceSession session;

    private readonly RunOptions runOptions;

    private readonly OrtIoBinding binding;

    private readonly OrtValue inputValue;

    private readonly OrtValue scoresValue;

    private readonly OrtValue boxesValue;

    private readonly float[] input;

    private readonly float[] scores;

    private readonly float[] boxes;

    private readonly FaceBox[] candidates;

    private readonly bool[] suppressed;

    private readonly float confidenceThreshold;

    private readonly float iouThreshold;

    public int InputWidth { get; }

    public int InputHeight { get; }

    // CHW RGB tensor normalized by (x - 127) / 128
    public Span<float> Input => input;

    public static TensorNormalization Normalization => TensorNormalization.Centered;

    public FaceDetector(string modelPath, FaceDetectorOptions options)
    {
        confidenceThreshold = options.ConfidenceThreshold;
        iouThreshold = options.IouThreshold;

        session = OnnxSession.Create(modelPath, options.IntraOpNumThreads, options.InterOpNumThreads);
        try
        {
            var inputName = session.InputNames[0];
            var inputDimensions = session.InputMetadata[inputName].Dimensions;
            if ((inputDimensions.Length != 4) || (inputDimensions[1] != 3))
            {
                throw new InvalidOperationException("Unsupported model input.");
            }

            InputHeight = inputDimensions[2];
            InputWidth = inputDimensions[3];

            var scoresName = FindOutput(session, ScoresName, 0);
            var boxesName = FindOutput(session, BoxesName, 1);
            var anchors = session.OutputMetadata[scoresName].Dimensions[1];
            if ((session.OutputMetadata[boxesName].Dimensions[1] != anchors) || (anchors <= 0))
            {
                throw new InvalidOperationException("Unsupported model output.");
            }

            input = new float[3 * InputWidth * InputHeight];
            scores = new float[anchors * 2];
            boxes = new float[anchors * 4];
            candidates = new FaceBox[anchors];
            suppressed = new bool[anchors];

            inputValue = OrtValue.CreateTensorValueFromMemory(input, [1, 3, InputHeight, InputWidth]);
            scoresValue = OrtValue.CreateTensorValueFromMemory(scores, [1, anchors, 2]);
            boxesValue = OrtValue.CreateTensorValueFromMemory(boxes, [1, anchors, 4]);

            runOptions = new RunOptions();
            binding = session.CreateIoBinding();
            binding.BindInput(inputName, inputValue);
            binding.BindOutput(scoresName, scoresValue);
            binding.BindOutput(boxesName, boxesValue);

            // Warm up
            session.RunWithBinding(runOptions, binding);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        binding?.Dispose();
        runOptions?.Dispose();
        inputValue?.Dispose();
        scoresValue?.Dispose();
        boxesValue?.Dispose();
        session.Dispose();
    }

    private static string FindOutput(InferenceSession session, string name, int index)
    {
        foreach (var outputName in session.OutputNames)
        {
            if (String.Equals(outputName, name, StringComparison.Ordinal))
            {
                return outputName;
            }
        }

        return session.OutputNames[index];
    }

    // Run inference on Input and write detected faces to results
    public int Detect(Span<FaceBox> results)
    {
        session.RunWithBinding(runOptions, binding);

        var count = Decode(scores, boxes, confidenceThreshold, candidates);
        return Suppress(candidates.AsSpan(0, count), suppressed, iouThreshold, results);
    }

    //--------------------------------------------------------------------------------
    // Post process
    //--------------------------------------------------------------------------------

    internal static int Decode(ReadOnlySpan<float> scores, ReadOnlySpan<float> boxes, float threshold, Span<FaceBox> candidates)
    {
        var anchors = Math.Min(Math.Min(scores.Length / 2, boxes.Length / 4), candidates.Length);
        var found = 0;
        for (var i = 0; i < anchors; i++)
        {
            var score = scores[(i * 2) + 1];
            if (score > threshold)
            {
                var offset = i * 4;
                candidates[found++] = new FaceBox(
                    Math.Clamp(boxes[offset], 0f, 1f),
                    Math.Clamp(boxes[offset + 1], 0f, 1f),
                    Math.Clamp(boxes[offset + 2], 0f, 1f),
                    Math.Clamp(boxes[offset + 3], 0f, 1f),
                    score);
            }
        }

        return found;
    }

    // Non-maximum suppression
    internal static int Suppress(Span<FaceBox> candidates, Span<bool> suppressed, float iouThreshold, Span<FaceBox> results)
    {
        candidates.Sort(static (x, y) => y.Confidence.CompareTo(x.Confidence));
        suppressed[..candidates.Length].Clear();

        var count = 0;
        for (var i = 0; (i < candidates.Length) && (count < results.Length); i++)
        {
            if (suppressed[i])
            {
                continue;
            }

            var current = candidates[i];
            results[count++] = current;
            for (var j = i + 1; j < candidates.Length; j++)
            {
                if (!suppressed[j] && (CalculateIou(current, candidates[j]) >= iouThreshold))
                {
                    suppressed[j] = true;
                }
            }
        }

        return count;
    }

    internal static float CalculateIou(FaceBox box1, FaceBox box2)
    {
        var left = Math.Max(box1.Left, box2.Left);
        var top = Math.Max(box1.Top, box2.Top);
        var right = Math.Min(box1.Right, box2.Right);
        var bottom = Math.Min(box1.Bottom, box2.Bottom);

        var intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        var area1 = (box1.Right - box1.Left) * (box1.Bottom - box1.Top);
        var area2 = (box2.Right - box2.Left) * (box2.Bottom - box2.Top);
        var union = area1 + area2 - intersection;
        return union > 0 ? intersection / union : 0;
    }
}
