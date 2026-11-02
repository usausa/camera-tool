namespace CameraTool.FaceViewer;

using BunnyTail.DependencyInjection;

using CameraTool.FaceViewer.Components;
using CameraTool.FaceViewer.Settings;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Serilog;

public static class ApplicationExtensions
{
    //--------------------------------------------------------------------------------
    // Container
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureContainer(this IHostApplicationBuilder builder)
    {
        builder.ConfigureContainer(new GeneratedServiceProviderFactory(static options => options.TrackTransientDisposables = false));

        return builder;
    }

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
    // Components
    //--------------------------------------------------------------------------------

    public static IHostApplicationBuilder ConfigureComponents(this IHostApplicationBuilder builder)
    {
        // System
        builder.Services.AddSingleton(TimeProvider.System);

        // Setting
        builder.Services.AddOptions<CameraSettings>().BindConfiguration("Camera").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<CameraSettings>>().Value);
        builder.Services.AddOptions<DisplaySettings>().BindConfiguration("Display").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<DisplaySettings>>().Value);
        builder.Services.AddOptions<DetectSettings>().BindConfiguration("Detect").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<DetectSettings>>().Value);
        builder.Services.AddOptions<MotionSettings>().BindConfiguration("Motion").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<MotionSettings>>().Value);
        builder.Services.AddOptions<AreaSettings>().BindConfiguration("Area").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<AreaSettings>>().Value);
        builder.Services.AddOptions<ObjectSettings>().BindConfiguration("Object").ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddSingleton(static p => p.GetRequiredService<IOptions<ObjectSettings>>().Value);

        // Components (the frame is handled in the order of the motion, the face and the object detection)
        builder.Services.AddSingleton<VideoSource>();
        builder.Services.AddHostedService(static p => p.GetRequiredService<VideoSource>());
        builder.Services.AddSingleton<AreaService>();
        builder.Services.AddSingleton<MotionService>();
        builder.Services.AddSingleton<DetectionGate>();
        builder.Services.AddSingleton<FaceDetectionService>();
        builder.Services.AddHostedService(static p => p.GetRequiredService<FaceDetectionService>());
        builder.Services.AddSingleton<ObjectDetectionService>();
        builder.Services.AddHostedService(static p => p.GetRequiredService<ObjectDetectionService>());

        // Window
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<MainWindowViewModel>();

        return builder;
    }
}
