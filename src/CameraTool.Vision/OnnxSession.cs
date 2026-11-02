namespace CameraTool.Vision;

using Microsoft.ML.OnnxRuntime;

// Inference session of the detectors, on the CPU
internal static class OnnxSession
{
    public static InferenceSession Create(string modelPath, int intraOpNumThreads, int interOpNumThreads)
    {
        using var options = new SessionOptions();
        // The face model has initializers in graph inputs, suppress the warnings
        options.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
        options.IntraOpNumThreads = intraOpNumThreads;
        options.InterOpNumThreads = interOpNumThreads;
        options.EnableCpuMemArena = true;
        options.EnableMemoryPattern = true;
        return new InferenceSession(modelPath, options);
    }
}
