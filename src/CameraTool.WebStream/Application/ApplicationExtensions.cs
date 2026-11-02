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
        builder.Services.AddOptions<DetectSettings>().BindConfiguration("Detect").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<DetectSettings>>().Value);

        // Components (the stream handles the frame before the detection)
        builder.Services.AddSingleton<VideoSource>();
        builder.Services.AddHostedService(static p => p.GetRequiredService<VideoSource>());
        builder.Services.AddSingleton<FrameHub>();
        builder.Services.AddSingleton<StreamService>();
        builder.Services.AddHostedService(static p => p.GetRequiredService<StreamService>());
        builder.Services.AddSingleton<FaceDetectionService>();
        builder.Services.AddHostedService(static p => p.GetRequiredService<FaceDetectionService>());
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

        app.Logger.InfoServiceStart();
        app.Logger.InfoServiceSettingsRuntime(RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription, RuntimeInformation.RuntimeIdentifier);
        app.Logger.InfoServiceSettingsEnvironment(typeof(Program).Assembly.GetName().Version, Environment.CurrentDirectory);
        app.Logger.InfoServiceSettingsGC(GCSettings.IsServerGC, GCSettings.LatencyMode, GCSettings.LargeObjectHeapCompactionMode);
        app.Logger.InfoServiceSettingsThreadPool(workerThreads, completionPortThreads);
        app.Logger.InfoServiceSettingsStream(streamSettings.Quality);
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
