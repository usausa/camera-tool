namespace CameraTool.FaceViewer;

using System.Runtime.InteropServices;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Smart.Mvvm.Resolver;

public sealed partial class App
{
    private readonly IHost host;

    private readonly ILogger<App> log;

    private bool showingError;

    //--------------------------------------------------------------------------------
    // Constructor
    //--------------------------------------------------------------------------------

    public App()
    {
        InitializeComponent();

        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        host = CreateHost();

        log = host.Services.GetRequiredService<ILogger<App>>();
        ResolveProvider.Default.Provider = host.Services;

        var environment = host.Services.GetRequiredService<IHostEnvironment>();
        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);

        log.InfoStartup();
        log.InfoStartupSettingsRuntime(RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription, RuntimeInformation.RuntimeIdentifier);
        log.InfoStartupSettingsGC(GCSettings.IsServerGC, GCSettings.LatencyMode, GCSettings.LargeObjectHeapCompactionMode);
        log.InfoStartupSettingsThreadPool(workerThreads, completionPortThreads);
        log.InfoStartupApplication(environment.ApplicationName, typeof(App).Assembly.GetName().Version);
        log.InfoStartupEnvironment(environment.EnvironmentName, environment.ContentRootPath);

        Current.DispatcherUnhandledException += (_, ea) =>
        {
            ShowError(ea.Exception);
            ea.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ea) => Dispatcher.Invoke(() => ShowError((Exception)ea.ExceptionObject));
        TaskScheduler.UnobservedTaskException += (_, ea) => log.ErrorUnobservedTaskException(ea.Exception);
    }

    private static IHost CreateHost()
    {
        var builder = Host.CreateApplicationBuilder(Environment.GetCommandLineArgs());

        // Container
        builder.ConfigureContainer();
        // Log
        builder.ConfigureLogging();
        // Components
        builder.ConfigureComponents();

        var host = builder.Build();
#if DEBUG
        if (host.Services is BunnyTail.DependencyInjection.GeneratedServiceProvider generatedProvider)
        {
            foreach (var line in BunnyTail.DependencyInjection.Diagnostics.ServiceFactoryReportExtensions.DescribeRuntimeFallbacks(generatedProvider).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                System.Diagnostics.Debug.WriteLine(line);
            }
        }
#endif
        return host;
    }

    //--------------------------------------------------------------------------------
    // Lifecycle
    //--------------------------------------------------------------------------------

    // ReSharper disable once AsyncVoidEventHandlerMethod
    protected override async void OnStartup(StartupEventArgs e)
    {
        var mainWindow = host.Services.GetRequiredService<MainWindow>();
        mainWindow.Closed += OnMainWindowClosed;
        MainWindow = mainWindow;
        mainWindow.Show();

        await host.StartAsync().ConfigureAwait(true);
    }

    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void OnMainWindowClosed(object? sender, EventArgs e)
    {
        try
        {
            await host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        }
        finally
        {
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        host.Dispose();
    }

    //--------------------------------------------------------------------------------
    // Error
    //--------------------------------------------------------------------------------

    private void ShowError(Exception ex)
    {
        log.ErrorUnknownException(ex);

        if (showingError)
        {
            return;
        }

        showingError = true;
        try
        {
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            showingError = false;
        }
    }
}
