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

        // Components
        builder.Services.AddSingleton<VideoSource>();
        builder.Services.AddHostedService(static p => p.GetRequiredService<VideoSource>());

        // Window
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<MainWindowViewModel>();

        return builder;
    }
}
