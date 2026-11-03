namespace CameraTool.WebStream.Endpoints;

using Microsoft.Extensions.Options;

public sealed class StartupTests
{
    // The streams encode only YUY2 and NV12, so another camera format stops the start (the settings are validated on start)
    [Fact]
    public Task UnsupportedFormatStopsStart() =>
        Assert.ThrowsAsync<OptionsValidationException>(static () =>
            StreamTestServer.RunAsync(
                new Dictionary<string, string?> { ["Camera:Format"] = "Bgra8" },
                static (_, _) => Task.CompletedTask));
}
