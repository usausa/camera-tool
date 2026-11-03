namespace CameraTool.WebStream.Application;

using System.Net;

internal static partial class Log
{
    // Startup

    [LoggerMessage(Level = LogLevel.Information, Message = "Service start.")]
    public static partial void InfoServiceStart(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Runtime: os=[{osDescription}], framework=[{frameworkDescription}], rid=[{runtimeIdentifier}]")]
    public static partial void InfoServiceSettingsRuntime(this ILogger logger, string osDescription, string frameworkDescription, string runtimeIdentifier);

    [LoggerMessage(Level = LogLevel.Information, Message = "Environment: version=[{version}], directory=[{directory}]")]
    public static partial void InfoServiceSettingsEnvironment(this ILogger logger, Version? version, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "GCSettings: serverGC=[{isServerGC}], latencyMode=[{latencyMode}], largeObjectHeapCompactionMode=[{largeObjectHeapCompactionMode}]")]
    public static partial void InfoServiceSettingsGC(this ILogger logger, bool isServerGC, GCLatencyMode latencyMode, GCLargeObjectHeapCompactionMode largeObjectHeapCompactionMode);

    [LoggerMessage(Level = LogLevel.Information, Message = "ThreadPool: workerThreads=[{workerThreads}], completionPortThreads=[{completionPortThreads}]")]
    public static partial void InfoServiceSettingsThreadPool(this ILogger logger, int workerThreads, int completionPortThreads);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stream: quality=[{quality}]")]
    public static partial void InfoServiceSettingsStream(this ILogger logger, int quality);

    [LoggerMessage(Level = LogLevel.Information, Message = "H264: enable=[{enable}], bitrate=[{bitrate}], keyFrameInterval=[{keyFrameInterval}], preferHardware=[{preferHardware}]")]
    public static partial void InfoServiceSettingsH264(this ILogger logger, bool enable, int bitrate, int keyFrameInterval, bool preferHardware);

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

    // Stream

    [LoggerMessage(Level = LogLevel.Information, Message = "Client connected. type=[{type}], address=[{address}], port=[{port}]")]
    public static partial void InfoClientConnected(this ILogger logger, string type, IPAddress? address, int port);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client disconnected. type=[{type}], address=[{address}], port=[{port}], sent=[{sent}], skipped=[{skipped}]")]
    public static partial void InfoClientDisconnected(this ILogger logger, string type, IPAddress? address, int port, long sent, long skipped);

    [LoggerMessage(Level = LogLevel.Error, Message = "Encode failed.")]
    public static partial void ErrorEncodeFailed(this ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Encode buffer expanded. capacity=[{capacity}]")]
    public static partial void DebugEncodeBufferExpanded(this ILogger logger, int capacity);

    // H.264

    [LoggerMessage(Level = LogLevel.Information, Message = "H.264 encoder created. name=[{name}], hardware=[{hardware}], size=[{width}x{height}], fullRange=[{fullRange}], fps=[{fps}], bitrate=[{bitrate}], keyFrameInterval=[{keyFrameInterval}], elapsed=[{elapsed:F1}ms]")]
    public static partial void InfoH264EncoderCreated(this ILogger logger, string name, bool hardware, int width, int height, bool fullRange, int fps, int bitrate, int keyFrameInterval, double elapsed);

    [LoggerMessage(Level = LogLevel.Error, Message = "H.264 encoder create failed. size=[{width}x{height}]")]
    public static partial void ErrorH264EncoderCreateFailed(this ILogger logger, Exception ex, int width, int height);

    [LoggerMessage(Level = LogLevel.Information, Message = "H.264 encoder released. name=[{name}]")]
    public static partial void InfoH264EncoderReleased(this ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Error, Message = "H.264 encoder failed. name=[{name}], code=[0x{code:X8}]")]
    public static partial void ErrorH264EncoderFailed(this ILogger logger, string name, int code);

    // Detection

    [LoggerMessage(Level = LogLevel.Information, Message = "Face detector loaded. model=[{model}], input=[{width}x{height}], elapsed=[{elapsed:F1}ms]")]
    public static partial void InfoFaceDetectorLoaded(this ILogger logger, string model, int width, int height, double elapsed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Face detector load failed. model=[{model}]")]
    public static partial void ErrorFaceDetectorLoadFailed(this ILogger logger, Exception ex, string model);

    [LoggerMessage(Level = LogLevel.Information, Message = "Face detection changed. enabled=[{enabled}]")]
    public static partial void InfoFaceDetectionChanged(this ILogger logger, bool enabled);

    [LoggerMessage(Level = LogLevel.Information, Message = "Motion gate changed. enabled=[{enabled}]")]
    public static partial void InfoMotionGateChanged(this ILogger logger, bool enabled);

    [LoggerMessage(Level = LogLevel.Information, Message = "Object detector loaded. model=[{model}], input=[{width}x{height}], elapsed=[{elapsed:F1}ms]")]
    public static partial void InfoObjectDetectorLoaded(this ILogger logger, string model, int width, int height, double elapsed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Object detector load failed. model=[{model}]")]
    public static partial void ErrorObjectDetectorLoadFailed(this ILogger logger, Exception ex, string model);

    [LoggerMessage(Level = LogLevel.Information, Message = "Object detection changed. enabled=[{enabled}]")]
    public static partial void InfoObjectDetectionChanged(this ILogger logger, bool enabled);

    // Motion

    [LoggerMessage(Level = LogLevel.Information, Message = "Motion detection changed. enabled=[{enabled}]")]
    public static partial void InfoMotionDetectionChanged(this ILogger logger, bool enabled);

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "H264: clients=[{clients}], encode=[{encodeFps:F1}], send=[{sendFps:F1}], skip=[{skipFps:F1}], drop=[{dropFps:F1}], key=[{keyFrameFps:F1}], input=[{inputTime:F3}/{inputTimeMax:F3}], encodeTime=[{encodeTime:F2}/{encodeTimeMax:F2}], streamLatency=[{streamLatency:F2}/{streamLatencyMax:F2}], sendStart=[{sendStart:F2}/{sendStartMax:F2}], sent=[{sent:F2}/{sentMax:F2}], bitrate=[{bitrate:F0}]")]
    public static partial void DebugH264Statistics(
        this ILogger logger,
        int clients,
        double encodeFps,
        double sendFps,
        double skipFps,
        double dropFps,
        double keyFrameFps,
        double inputTime,
        double inputTimeMax,
        double encodeTime,
        double encodeTimeMax,
        double streamLatency,
        double streamLatencyMax,
        double sendStart,
        double sendStartMax,
        double sent,
        double sentMax,
        double bitrate);

    // name: Detection or Object, countName: faces or objects
    [LoggerMessage(Level = LogLevel.Debug, Message = "{name}: enabled=[{enabled}], detect=[{detectFps:F1}], preprocess=[{preprocessTime:F3}/{preprocessTimeMax:F3}], inference=[{inferenceTime:F2}/{inferenceTimeMax:F2}], {countName}=[{count}]")]
    public static partial void DebugDetectorStatistics(this ILogger logger, string name, bool enabled, double detectFps, double preprocessTime, double preprocessTimeMax, double inferenceTime, double inferenceTimeMax, string countName, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Motion: enabled=[{enabled}], gate=[{gate}], moving=[{moving}], level=[{level:F1}], time=[{time:F3}/{timeMax:F3}]")]
    public static partial void DebugMotionStatistics(this ILogger logger, bool enabled, bool gate, bool moving, double level, double time, double timeMax);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Statistics: format=[{format}], clients=[{clients}], capture=[{captureFps:F1}], encode=[{encodeFps:F1}], send=[{sendFps:F1}], skip=[{skipFps:F1}], captureLatency=[{captureLatency:F2}/{captureLatencyMax:F2}], convert=[{convertTime:F3}/{convertTimeMax:F3}], encodeTime=[{encodeTime:F2}/{encodeTimeMax:F2}], streamLatency=[{streamLatency:F2}/{streamLatencyMax:F2}], sendStart=[{sendStart:F2}/{sendStartMax:F2}], sent=[{sent:F2}/{sentMax:F2}], size=[{size:F1}], cpu=[{cpuUsage:F2}], allocated=[{allocated:F1}], gc=[{gc0:F1}/{gc1:F1}/{gc2:F1}]")]
    public static partial void DebugStatistics(
        this ILogger logger,
        string format,
        int clients,
        double captureFps,
        double encodeFps,
        double sendFps,
        double skipFps,
        double captureLatency,
        double captureLatencyMax,
        double convertTime,
        double convertTimeMax,
        double encodeTime,
        double encodeTimeMax,
        double streamLatency,
        double streamLatencyMax,
        double sendStart,
        double sendStartMax,
        double sent,
        double sentMax,
        double size,
        double cpuUsage,
        double allocated,
        double gc0,
        double gc1,
        double gc2);
}
