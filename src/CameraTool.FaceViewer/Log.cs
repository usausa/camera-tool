namespace CameraTool.FaceViewer;

internal static partial class Log
{
    // Startup

    [LoggerMessage(Level = LogLevel.Information, Message = "Application start.")]
    public static partial void InfoStartup(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Runtime: os=[{osDescription}], framework=[{frameworkDescription}], rid=[{runtimeIdentifier}]")]
    public static partial void InfoStartupSettingsRuntime(this ILogger logger, string osDescription, string frameworkDescription, string runtimeIdentifier);

    [LoggerMessage(Level = LogLevel.Information, Message = "GCSettings: serverGC=[{isServerGC}], latencyMode=[{latencyMode}], largeObjectHeapCompactionMode=[{largeObjectHeapCompactionMode}]")]
    public static partial void InfoStartupSettingsGC(this ILogger logger, bool isServerGC, GCLatencyMode latencyMode, GCLargeObjectHeapCompactionMode largeObjectHeapCompactionMode);

    [LoggerMessage(Level = LogLevel.Information, Message = "ThreadPool: workerThreads=[{workerThreads}], completionPortThreads=[{completionPortThreads}]")]
    public static partial void InfoStartupSettingsThreadPool(this ILogger logger, int workerThreads, int completionPortThreads);

    [LoggerMessage(Level = LogLevel.Information, Message = "Application: application=[{application}], version=[{version}]")]
    public static partial void InfoStartupApplication(this ILogger logger, string application, Version? version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Environment: environment=[{environment}], contentRoot=[{contentRoot}]")]
    public static partial void InfoStartupEnvironment(this ILogger logger, string environment, string contentRoot);

    // Error

    [LoggerMessage(Level = LogLevel.Error, Message = "Unknown exception.")]
    public static partial void ErrorUnknownException(this ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unobserved task exception.")]
    public static partial void ErrorUnobservedTaskException(this ILogger logger, Exception ex);

    // Camera

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera devices found. count=[{count}]")]
    public static partial void InfoCameraDevices(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Camera not found. name=[{name}]")]
    public static partial void WarnCameraNotFound(this ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Camera format not supported. device=[{device}], format=[{format}]")]
    public static partial void WarnCameraFormatNotFound(this ILogger logger, string device, string format);

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera started. device=[{device}], format=[{format}], output=[{output}], conversion=[{conversion}]")]
    public static partial void InfoCameraStarted(this ILogger logger, string device, string format, string output, string conversion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera stopped.")]
    public static partial void InfoCameraStopped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Camera start failed. device=[{device}], format=[{format}]")]
    public static partial void ErrorCameraStartFailed(this ILogger logger, Exception ex, string device, string format);

    [LoggerMessage(Level = LogLevel.Error, Message = "Camera enumeration failed.")]
    public static partial void ErrorCameraEnumerationFailed(this ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Camera failed. message=[{message}], code=[0x{code:X8}]")]
    public static partial void ErrorCameraFailed(this ILogger logger, string message, uint code);

    // Detection

    [LoggerMessage(Level = LogLevel.Information, Message = "Face detector loaded. model=[{model}], input=[{width}x{height}], elapsed=[{elapsed:F1}ms]")]
    public static partial void InfoFaceDetectorLoaded(this ILogger logger, string model, int width, int height, double elapsed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Face detector load failed. model=[{model}]")]
    public static partial void ErrorFaceDetectorLoadFailed(this ILogger logger, Exception ex, string model);

    [LoggerMessage(Level = LogLevel.Information, Message = "Object detector loaded. model=[{model}], input=[{width}x{height}], elapsed=[{elapsed:F1}ms]")]
    public static partial void InfoObjectDetectorLoaded(this ILogger logger, string model, int width, int height, double elapsed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Object detector load failed. model=[{model}]")]
    public static partial void ErrorObjectDetectorLoadFailed(this ILogger logger, Exception ex, string model);

    // Area

    [LoggerMessage(Level = LogLevel.Information, Message = "Area loaded. path=[{path}], cells=[{cells}]")]
    public static partial void InfoAreaLoaded(this ILogger logger, string path, int cells);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Area file is invalid. path=[{path}]")]
    public static partial void WarnAreaInvalid(this ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Area load failed. path=[{path}]")]
    public static partial void WarnAreaLoadFailed(this ILogger logger, Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Area saved. path=[{path}], cells=[{cells}]")]
    public static partial void InfoAreaSaved(this ILogger logger, string path, int cells);

    [LoggerMessage(Level = LogLevel.Error, Message = "Area save failed. path=[{path}]")]
    public static partial void ErrorAreaSaveFailed(this ILogger logger, Exception ex, string path);

    // Statistics

    // name: Detection or Object, resultsName: faces or objects
    [LoggerMessage(Level = LogLevel.Debug, Message = "{name}: enabled=[{enabled}], detect=[{detectFps:F1}], preprocess=[{preprocessTime:F3}/{preprocessTimeMax:F3}], inference=[{inferenceTime:F2}/{inferenceTimeMax:F2}], {resultsName}=[{results}]")]
    public static partial void DebugDetectorStatistics(this ILogger logger, string name, bool enabled, double detectFps, double preprocessTime, double preprocessTimeMax, double inferenceTime, double inferenceTimeMax, string resultsName, string results);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Motion: enabled=[{enabled}], gate=[{gate}], status=[{status}], level=[{level:F1}], time=[{time:F3}/{timeMax:F3}]")]
    public static partial void DebugMotionStatistics(this ILogger logger, bool enabled, bool gate, string status, double level, double time, double timeMax);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Statistics: format=[{format}], output=[{output}], update=[{update}], capture=[{captureFps:F1}], display=[{displayFps:F1}], captureLatency=[{captureLatency:F2}/{captureLatencyMax:F2}], process=[{processTime:F3}/{processTimeMax:F3}], displayLatency=[{displayLatency:F2}/{displayLatencyMax:F2}], cpu=[{cpuUsage:F2}], allocated=[{allocated:F1}], gc=[{gc0:F1}/{gc1:F1}/{gc2:F1}]")]
    public static partial void DebugStatistics(
        this ILogger logger,
        string format,
        string output,
        string update,
        double captureFps,
        double displayFps,
        double captureLatency,
        double captureLatencyMax,
        double processTime,
        double processTimeMax,
        double displayLatency,
        double displayLatencyMax,
        double cpuUsage,
        double allocated,
        double gc0,
        double gc1,
        double gc2);
}
