namespace CameraTool.WebStream.Endpoints;

using CameraTool.WebStream.Components;

using Microsoft.AspNetCore.Http.HttpResults;

public static class StatusEndpoints
{
    //--------------------------------------------------------------------------------
    // Mapping
    //--------------------------------------------------------------------------------

    public static void MapStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/api/status", HandleStatus);
    }

    //--------------------------------------------------------------------------------
    // Status
    //--------------------------------------------------------------------------------

    private static Ok<StreamStatus> HandleStatus(StatusService status) =>
        TypedResults.Ok(status.Current);
}
