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

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera started. device=[{device}], format=[{format}], output=[{output}]")]
    public static partial void InfoCameraStarted(this ILogger logger, string device, string format, string output);

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera stopped.")]
    public static partial void InfoCameraStopped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Camera start failed. device=[{device}], format=[{format}]")]
    public static partial void ErrorCameraStartFailed(this ILogger logger, Exception ex, string device, string format);

    [LoggerMessage(Level = LogLevel.Error, Message = "Camera enumeration failed.")]
    public static partial void ErrorCameraEnumerationFailed(this ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Camera failed. message=[{message}], code=[0x{code:X8}]")]
    public static partial void ErrorCameraFailed(this ILogger logger, string message, uint code);
}
