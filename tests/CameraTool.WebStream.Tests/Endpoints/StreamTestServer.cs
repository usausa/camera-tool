namespace CameraTool.WebStream.Endpoints;

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using CameraTool.WebStream.Application;
using CameraTool.WebStream.Components;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// WebStream on a free port of the loopback address, with the test camera instead of a real one.
// The settings are only the values given here (the appsettings of the application are not read).
internal sealed class StreamTestServer : IAsyncDisposable
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly WebApplication app;

    private readonly string directory;

    public Uri BaseAddress { get; }

    public HttpClient Client { get; }

    public TestCameraProvider Camera { get; }

    // File of the detection area, in the folder of the server
    public string AreaPath => Path.Combine(directory, "area.json");

    private StreamTestServer(WebApplication app, string directory, TestCameraProvider camera)
    {
        this.app = app;
        this.directory = directory;
        Camera = camera;
        BaseAddress = new Uri(app.Urls.First());
        Client = new HttpClient { BaseAddress = BaseAddress };
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.StopAsync().ConfigureAwait(true);
        await app.DisposeAsync().ConfigureAwait(true);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    // Runs the test with a server started for it, within the test timeout.
    // settings: values that replace the defaults (for example "H264:Enable")
    public static async Task RunAsync(IReadOnlyDictionary<string, string?>? settings, Func<StreamTestServer, CancellationToken, Task> test)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TestTimeout);
        var server = await StartAsync(settings, timeout.Token).ConfigureAwait(true);
        try
        {
            await test(server, timeout.Token).ConfigureAwait(true);
        }
        finally
        {
            await server.DisposeAsync().ConfigureAwait(true);
        }
    }

    private static async Task<StreamTestServer> StartAsync(IReadOnlyDictionary<string, string?>? settings, CancellationToken cancellationToken)
    {
        // The content root is an empty folder, so the appsettings of the application are not read
        var directory = Path.Combine(Path.GetTempPath(), "CameraTool.WebStream.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var values = new Dictionary<string, string?>
        {
            ["Camera:Width"] = TestCameraProvider.Width.ToString(CultureInfo.InvariantCulture),
            ["Camera:Height"] = TestCameraProvider.Height.ToString(CultureInfo.InvariantCulture),
            ["Camera:Fps"] = TestCameraProvider.FrameRate.ToString(CultureInfo.InvariantCulture),
            ["Area:Path"] = Path.Combine(directory, "area.json"),
            ["Detect:Enable"] = "false",
            ["Object:Enable"] = "false",
            // The software encoder is on every PC
            ["H264:PreferHardware"] = "false"
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production, ContentRootPath = directory });
        builder.Configuration.AddInMemoryCollection(values);
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.ConfigureHttp();
        builder.ConfigureComponents();
        // Replaces the cameras (the last registration is used)
        var camera = new TestCameraProvider();
        builder.Services.AddSingleton<ICameraProvider>(camera);

        var app = builder.Build();
        app.UseWebSockets();
        app.MapEndpoints();
        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(true);
            return new StreamTestServer(app, directory, camera);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(true);
            Directory.Delete(directory, true);
            throw;
        }
    }

    //--------------------------------------------------------------------------------
    // Request
    //--------------------------------------------------------------------------------

    public Uri GetWebSocketUri(string path) => new UriBuilder(BaseAddress) { Scheme = "ws", Path = path }.Uri;

    public async Task<JsonElement> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), cancellationToken).ConfigureAwait(true);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true));
    }

    public async Task<HttpStatusCode> PutJsonAsync(string path, string json, CancellationToken cancellationToken)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await Client.PutAsync(new Uri(path, UriKind.Relative), content, cancellationToken).ConfigureAwait(true);
        return response.StatusCode;
    }

    // The status is updated every second, so it is read until the condition is met
    public async Task<JsonElement> WaitForStatusAsync(Func<JsonElement, bool> condition, CancellationToken cancellationToken)
    {
        while (true)
        {
            var status = await GetJsonAsync("/api/status", cancellationToken).ConfigureAwait(true);
            if (condition(status))
            {
                return status;
            }

            await Task.Delay(StatusPollInterval, cancellationToken).ConfigureAwait(true);
        }
    }
}
