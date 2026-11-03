namespace CameraTool.WebStream.Endpoints;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using CameraTool.Vision;

public sealed class DetectionEndpointsTests
{
    private const int Columns = 32;

    private const int Rows = 24;

    private static readonly string FullRow = new('1', Columns);

    //--------------------------------------------------------------------------------
    // Area
    //--------------------------------------------------------------------------------

    [Fact]
    public Task AreaIsUpdatedAndSaved() =>
        StreamTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Arrange (full at first)
            var initial = await server.GetJsonAsync("/api/area", cancellationToken).ConfigureAwait(true);
            var version = (await server.WaitForStatusAsync(static x => x.GetProperty("areaCells").GetInt32() > 0, cancellationToken).ConfigureAwait(true)).GetProperty("areaVersion").GetInt32();
            var area = CreateArea(Columns, Rows, new string('1', Columns / 2) + new string('0', Columns / 2));

            // Act (the right half is outside)
            var result = await PutAreaAsync(server, area, cancellationToken).ConfigureAwait(true);
            var updated = await server.GetJsonAsync("/api/area", cancellationToken).ConfigureAwait(true);
            var status = await server.WaitForStatusAsync(x => x.GetProperty("areaVersion").GetInt32() != version, cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(Columns, initial.GetProperty("columns").GetInt32());
            Assert.Equal(Rows, initial.GetProperty("rows").GetInt32());
            Assert.All(ReadCells(initial), static x => Assert.Equal(FullRow, x));
            Assert.Equal(HttpStatusCode.NoContent, result);
            Assert.Equal(area.Cells, ReadCells(updated));
            Assert.Equal(Columns * Rows / 2, status.GetProperty("areaCells").GetInt32());
            Assert.True(File.Exists(server.AreaPath));
        });

    [Theory]
    [InlineData(16, Rows, 16)]
    [InlineData(Columns, Rows - 1, Columns)]
    [InlineData(Columns, Rows, Columns - 1)]
    public Task InvalidAreaIsBadRequest(int columns, int rows, int rowLength) =>
        StreamTestServer.RunAsync(null, async (server, cancellationToken) =>
        {
            // Act
            var result = await PutAreaAsync(server, CreateArea(columns, rows, new string('1', rowLength)), cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, result);
        });

    [Fact]
    public Task AreaSaveFailureIsServerError() =>
        StreamTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Arrange (a folder takes the place of the file)
            Directory.CreateDirectory(server.AreaPath);
            var area = CreateArea(Columns, Rows, new string('0', Columns / 2) + new string('1', Columns / 2));

            // Act
            using var content = JsonContent.Create(area, ApiJsonContext.Default.DetectionAreaData);
            using var response = await server.Client.PutAsync(new Uri("/api/area", UriKind.Relative), content, cancellationToken).ConfigureAwait(true);
            var problem = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true));
            var used = await server.GetJsonAsync("/api/area", cancellationToken).ConfigureAwait(true);

            // Assert (the area is used until the server stops)
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("The area could not be saved to the file.", problem.GetProperty("detail").GetString());
            Assert.Equal(area.Cells, ReadCells(used));
        });

    //--------------------------------------------------------------------------------
    // Detection
    //--------------------------------------------------------------------------------

    [Fact]
    public Task DetectionSettingsAreChanged() =>
        StreamTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Act
            var detection = await server.PutJsonAsync("/api/detection", """{"enabled":true,"motionGate":true}""", cancellationToken).ConfigureAwait(true);
            var motion = await server.PutJsonAsync("/api/motion", """{"enabled":false}""", cancellationToken).ConfigureAwait(true);
            var objects = await server.PutJsonAsync("/api/objects", """{"enabled":true}""", cancellationToken).ConfigureAwait(true);
            var status = await server.WaitForStatusAsync(
                static x => x.GetProperty("faces").GetProperty("enabled").GetBoolean() && x.GetProperty("motionGate").GetBoolean() && !x.GetProperty("motionEnabled").GetBoolean() && x.GetProperty("objects").GetProperty("enabled").GetBoolean(),
                cancellationToken).ConfigureAwait(true);

            // Assert (the status shows all the changes)
            Assert.Equal(HttpStatusCode.NoContent, detection);
            Assert.Equal(HttpStatusCode.NoContent, motion);
            Assert.Equal(HttpStatusCode.NoContent, objects);
            Assert.True(status.GetProperty("objects").GetProperty("enabled").GetBoolean());
        });

    [Fact]
    public Task ObjectLabelsAreCocoClasses() =>
        StreamTestServer.RunAsync(null, static async (server, cancellationToken) =>
        {
            // Act
            var labels = await server.GetJsonAsync("/api/objects/labels", cancellationToken).ConfigureAwait(true);

            // Assert
            Assert.Equal(80, labels.GetArrayLength());
            AssertLabel(labels[0], "person", "person");
            AssertLabel(labels[2], "car", "vehicle");
            AssertLabel(labels[16], "dog", "animal");
            AssertLabel(labels[79], "toothbrush", "other");
        });

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

    private static DetectionAreaData CreateArea(int columns, int rows, string row) =>
        new() { Columns = columns, Rows = rows, Cells = Enumerable.Repeat(row, rows).ToArray() };

    private static async Task<HttpStatusCode> PutAreaAsync(StreamTestServer server, DetectionAreaData area, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(area, ApiJsonContext.Default.DetectionAreaData);
        using var response = await server.Client.PutAsync(new Uri("/api/area", UriKind.Relative), content, cancellationToken).ConfigureAwait(true);
        return response.StatusCode;
    }

    private static string[] ReadCells(JsonElement area) =>
        area.GetProperty("cells").EnumerateArray().Select(static x => x.GetString() ?? string.Empty).ToArray();

    private static void AssertLabel(JsonElement label, string name, string group)
    {
        Assert.Equal(name, label.GetProperty("name").GetString());
        Assert.Equal(group, label.GetProperty("group").GetString());
    }
}
