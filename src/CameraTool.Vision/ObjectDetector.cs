namespace CameraTool.Vision;

using Microsoft.ML.OnnxRuntime;

// D-FINE (DETR style, no NMS)
// Input pixel_values [1, 3, H, W]: RGB 0-1, stretched to the input size
// Outputs logits [1, Q, 80] (before sigmoid) and pred_boxes [1, Q, 4] (normalized center x, center y, width, height)
// Inputs and outputs are bound to pre-allocated buffers, so detection does not allocate
public sealed class ObjectDetector : IDisposable
{
    private const string InputName = "pixel_values";

    private const string LogitsName = "logits";

    private const string BoxesName = "pred_boxes";

    private readonly InferenceSession session;

    private readonly RunOptions runOptions;

    private readonly OrtIoBinding binding;

    private readonly OrtValue inputValue;

    private readonly OrtValue logitsValue;

    private readonly OrtValue boxesValue;

    private readonly float[] input;

    private readonly float[] logits;

    private readonly float[] boxes;

    private readonly ObjectBox[] candidates;

    private readonly bool[] targets;

    // The logits are compared with the threshold converted to a logit, so the sigmoid is calculated only for the results
    private readonly float logitThreshold;

    public int InputWidth { get; }

    public int InputHeight { get; }

    // CHW RGB tensor normalized by x / 255
    public Span<float> Input => input;

    public static TensorNormalization Normalization => TensorNormalization.Unit;

    public ObjectDetector(string modelPath, ObjectDetectorOptions options)
    {
        logitThreshold = ToLogit(options.ConfidenceThreshold);
        targets = CreateTargets(options.Classes);

        using var sessionOptions = new SessionOptions();
        sessionOptions.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;
        sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        sessionOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
        sessionOptions.IntraOpNumThreads = options.IntraOpNumThreads;
        sessionOptions.InterOpNumThreads = options.InterOpNumThreads;
        sessionOptions.EnableCpuMemArena = true;
        sessionOptions.EnableMemoryPattern = true;

        session = new InferenceSession(modelPath, sessionOptions);
        try
        {
            if (!session.InputMetadata.TryGetValue(InputName, out var inputMetadata) ||
                (inputMetadata.Dimensions is not [1, 3, > 0, > 0]))
            {
                throw new InvalidOperationException("Unsupported model input.");
            }

            InputHeight = inputMetadata.Dimensions[2];
            InputWidth = inputMetadata.Dimensions[3];

            if (!session.OutputMetadata.TryGetValue(LogitsName, out var logitsMetadata) ||
                !session.OutputMetadata.TryGetValue(BoxesName, out var boxesMetadata) ||
                (logitsMetadata.Dimensions is not [1, > 0, var classes]) ||
                (classes != ObjectLabels.Count) ||
                (boxesMetadata.Dimensions is not [1, var queries, 4]) ||
                (queries != logitsMetadata.Dimensions[1]))
            {
                throw new InvalidOperationException("Unsupported model output.");
            }

            input = new float[3 * InputWidth * InputHeight];
            logits = new float[queries * classes];
            boxes = new float[queries * 4];
            candidates = new ObjectBox[queries];

            inputValue = OrtValue.CreateTensorValueFromMemory(input, [1, 3, InputHeight, InputWidth]);
            logitsValue = OrtValue.CreateTensorValueFromMemory(logits, [1, queries, classes]);
            boxesValue = OrtValue.CreateTensorValueFromMemory(boxes, [1, queries, 4]);

            runOptions = new RunOptions();
            binding = session.CreateIoBinding();
            binding.BindInput(InputName, inputValue);
            binding.BindOutput(LogitsName, logitsValue);
            binding.BindOutput(BoxesName, boxesValue);

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
        logitsValue?.Dispose();
        boxesValue?.Dispose();
        session.Dispose();
    }

    private static bool[] CreateTargets(IReadOnlyList<int> classes)
    {
        var result = new bool[ObjectLabels.Count];
        if (classes.Count == 0)
        {
            result.AsSpan().Fill(true);
            return result;
        }

        foreach (var classId in classes)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(classId, nameof(classes));
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(classId, ObjectLabels.Count, nameof(classes));
            result[classId] = true;
        }

        return result;
    }

    // Run inference on Input and write the detected objects to results in descending order of the confidence
    public int Detect(Span<ObjectBox> results)
    {
        session.RunWithBinding(runOptions, binding);

        var count = Decode(logits, boxes, ObjectLabels.Count, logitThreshold, targets, candidates);
        return Select(candidates.AsSpan(0, count), results);
    }

    //--------------------------------------------------------------------------------
    // Post process
    //--------------------------------------------------------------------------------

    internal static float ToLogit(float probability) =>
        probability <= 0f ? Single.NegativeInfinity : probability >= 1f ? Single.PositiveInfinity : MathF.Log(probability / (1f - probability));

    // For each query, the target class with the highest logit is taken when it is above the threshold
    internal static int Decode(ReadOnlySpan<float> logits, ReadOnlySpan<float> boxes, int classes, float logitThreshold, ReadOnlySpan<bool> targets, Span<ObjectBox> candidates)
    {
        var queries = Math.Min(Math.Min(logits.Length / classes, boxes.Length / 4), candidates.Length);
        var found = 0;
        for (var q = 0; q < queries; q++)
        {
            var row = logits.Slice(q * classes, classes);
            var best = -1;
            var bestLogit = Single.NegativeInfinity;
            for (var c = 0; c < classes; c++)
            {
                if (targets[c] && (row[c] > bestLogit))
                {
                    best = c;
                    bestLogit = row[c];
                }
            }

            if ((best < 0) || (bestLogit < logitThreshold))
            {
                continue;
            }

            var box = boxes.Slice(q * 4, 4);
            var halfWidth = box[2] * 0.5f;
            var halfHeight = box[3] * 0.5f;
            candidates[found++] = new ObjectBox(
                Math.Clamp(box[0] - halfWidth, 0f, 1f),
                Math.Clamp(box[1] - halfHeight, 0f, 1f),
                Math.Clamp(box[0] + halfWidth, 0f, 1f),
                Math.Clamp(box[1] + halfHeight, 0f, 1f),
                1f / (1f + MathF.Exp(-bestLogit)),
                best);
        }

        return found;
    }

    // Highest confidence first, up to the capacity of the results
    internal static int Select(Span<ObjectBox> candidates, Span<ObjectBox> results)
    {
        candidates.Sort(static (x, y) => y.Confidence.CompareTo(x.Confidence));
        var count = Math.Min(candidates.Length, results.Length);
        candidates[..count].CopyTo(results);
        return count;
    }
}
