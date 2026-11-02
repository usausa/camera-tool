namespace CameraTool.WebStream.Endpoints;

using CameraTool.WebStream.Components;

using Microsoft.AspNetCore.Http.HttpResults;

//--------------------------------------------------------------------------------
// Models
//--------------------------------------------------------------------------------

public sealed class DetectionRequest
{
    public bool Enabled { get; set; }
}

//--------------------------------------------------------------------------------
// Endpoints
//--------------------------------------------------------------------------------

public static class DetectionEndpoints
{
    //--------------------------------------------------------------------------------
    // Mapping
    //--------------------------------------------------------------------------------

    public static void MapDetectionEndpoints(this WebApplication app)
    {
        app.MapPut("/api/detection", HandleUpdate);
    }

    //--------------------------------------------------------------------------------
    // Update
    //--------------------------------------------------------------------------------

    private static NoContent HandleUpdate(DetectionRequest request, FaceDetectionService detection)
    {
        detection.IsEnabled = request.Enabled;
        return TypedResults.NoContent();
    }
}
