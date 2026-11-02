namespace CameraTool.WebStream.Application;

using System.Runtime.InteropServices;

using CameraTool.WebStream.Components;
using CameraTool.WebStream.Endpoints;
using CameraTool.WebStream.Settings;

using Serilog;

public static class ApplicationExtensions
{
    //--------------------------------------------------------------------------------
    // Logging
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureLogging(this IHostApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Services.AddSerilog(options => options.ReadFrom.Configuration(builder.Configuration));

        return builder;
    }

    //--------------------------------------------------------------------------------
    // Http
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureHttp(this WebApplicationBuilder builder)
    {
        // wwwroot is copied only on publish, serve it from the project when running the build output
        builder.WebHost.UseStaticWebAssets();

        // JSON
        builder.Services.ConfigureHttpJsonOptions(static options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
        });

        return builder;
    }

    //--------------------------------------------------------------------------------
    // Components
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureComponents(this IHostApplicationBuilder builder)
    {
        // System
        builder.Services.AddSingleton(TimeProvider.System);

        // Setting
        builder.Services.AddOptions<CameraSettings>().BindConfiguration("Camera").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<CameraSettings>>().Value);
        builder.Services.AddOptions<StreamSettings>().BindConfiguration("Stream").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<StreamSettings>>().Value);
        builder.Services.AddOptions<H264Settings>().BindConfiguration("H264").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<H264Settings>>().Value);
        builder.Services.AddOptions<DetectSettings>().BindConfiguration("Detect").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<DetectSettings>>().Value);
        builder.Services.AddOptions<MotionSettings>().BindConfiguration("Motion").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<MotionSettings>>().Value);
        builder.Services.AddOptions<AreaSettings>().BindConfiguration("Area").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<AreaSettings>>().Value);
        builder.Services.AddOptions<ObjectSettings>().BindConfiguration("Object").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<ObjectSettings>>().Value);

        // Components (the frame is handled in the order of the JPEG stream, the H.264 stream, the motion, the face and the object detection)
        builder.Services.AddSingleton<VideoSource>();
        builder.Services.AddHostedService(static p => p.GetRequiredService<VideoSource>());
        builder.Services.AddSingleton<AreaService>();
        builder.Services.AddSingleton<StreamHubs>();
        builder.Services.AddSingleton<StreamService>();
        builder.Services.AddHostedService(static p => p.GetRequiredService<StreamService>());
        builder.Services.AddSingleton<H264StreamService>();
        builder.Services.AddHostedService(static p => p.GetRequiredService<H264StreamService>());
        builder.Services.AddSingleton<MotionService>();
        builder.Services.AddHostedService(static p => p.GetRequiredService<MotionService>());
        builder.Services.AddSingleton<DetectionGate>();
        builder.Services.AddSingleton<FaceDetectionService>();
        builder.Services.AddHostedService(static p => p.GetRequiredService<FaceDetectionService>());
        builder.Services.AddSingleton<ObjectDetectionService>();
        builder.Services.AddHostedService(static p => p.GetRequiredService<ObjectDetectionService>());
        builder.Services.AddSingleton<StatusService>();
        builder.Services.AddHostedService(static p => p.GetRequiredService<StatusService>());

        return builder;
    }

    //--------------------------------------------------------------------------------
    // Information
    //--------------------------------------------------------------------------------

    public static void LogStartupInformation(this WebApplication app)
    {
        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);

        var streamSettings = app.Services.GetRequiredService<StreamSettings>();
        var h264Settings = app.Services.GetRequiredService<H264Settings>();

        app.Logger.InfoServiceStart();
        app.Logger.InfoServiceSettingsRuntime(RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription, RuntimeInformation.RuntimeIdentifier);
        app.Logger.InfoServiceSettingsEnvironment(typeof(Program).Assembly.GetName().Version, Environment.CurrentDirectory);
        app.Logger.InfoServiceSettingsGC(GCSettings.IsServerGC, GCSettings.LatencyMode, GCSettings.LargeObjectHeapCompactionMode);
        app.Logger.InfoServiceSettingsThreadPool(workerThreads, completionPortThreads);
        app.Logger.InfoServiceSettingsStream(streamSettings.Quality);
        app.Logger.InfoServiceSettingsH264(h264Settings.Enable, h264Settings.Bitrate, h264Settings.KeyFrameInterval, h264Settings.PreferHardware);
    }

    //--------------------------------------------------------------------------------
    // End point
    //--------------------------------------------------------------------------------

    public static WebApplication MapEndpoints(this WebApplication app)
    {
        app.MapStreamEndpoints();
        app.MapStatusEndpoints();
        app.MapDetectionEndpoints();

        return app;
    }
}
