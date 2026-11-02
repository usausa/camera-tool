namespace CameraTool.WebStream.Endpoints;

using CameraTool.WebStream.Components;

using Microsoft.AspNetCore.Http.HttpResults;

//--------------------------------------------------------------------------------
// Models
//--------------------------------------------------------------------------------

// Only the specified values are changed
public sealed class DetectionRequest
{
    public bool? Enabled { get; set; }

    // Face detection only on motion
    public bool? MotionGate { get; set; }
}

public sealed class MotionRequest
{
    public bool? Enabled { get; set; }
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
        app.MapPut("/api/detection", HandleDetectionUpdate);
        app.MapPut("/api/motion", HandleMotionUpdate);
        app.MapGet("/api/area", HandleArea);
        app.MapPut("/api/area", HandleAreaUpdate);
    }

    //--------------------------------------------------------------------------------
    // Update
    //--------------------------------------------------------------------------------

    private static NoContent HandleDetectionUpdate(DetectionRequest request, FaceDetectionService detection)
    {
        if (request.Enabled is { } enabled)
        {
            detection.IsEnabled = enabled;
        }

        if (request.MotionGate is { } motionGate)
        {
            detection.IsMotionGateEnabled = motionGate;
        }

        return TypedResults.NoContent();
    }

    private static NoContent HandleMotionUpdate(MotionRequest request, MotionService motion)
    {
        if (request.Enabled is { } enabled)
        {
            motion.IsEnabled = enabled;
        }

        return TypedResults.NoContent();
    }

    //--------------------------------------------------------------------------------
    // Area
    //--------------------------------------------------------------------------------

    private static Ok<AreaData> HandleArea(AreaService area) =>
        TypedResults.Ok(AreaData.From(area.Current));

    // Applied to the next frame and saved to the file
    private static Results<NoContent, BadRequest> HandleAreaUpdate(AreaData request, AreaService area)
    {
        if (!request.TryGetArea(out var value))
        {
            return TypedResults.BadRequest();
        }

        area.Update(value);
        area.Save();
        return TypedResults.NoContent();
    }
}
