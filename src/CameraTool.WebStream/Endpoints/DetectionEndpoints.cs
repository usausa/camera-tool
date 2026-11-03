namespace CameraTool.WebStream.Endpoints;

using CameraTool.Vision;
using CameraTool.WebStream.Services;

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

public sealed class ObjectRequest
{
    public bool? Enabled { get; set; }
}

// Group: person, animal, vehicle or other
public sealed record ObjectLabel(string Name, string Group);

//--------------------------------------------------------------------------------
// Endpoints
//--------------------------------------------------------------------------------

public static class DetectionEndpoints
{
    // In the order of the class ids
    private static readonly ObjectLabel[] Labels = CreateLabels();

    //--------------------------------------------------------------------------------
    // Mapping
    //--------------------------------------------------------------------------------

    public static void MapDetectionEndpoints(this WebApplication app)
    {
        app.MapPut("/api/detection", HandleDetectionUpdate);
        app.MapPut("/api/motion", HandleMotionUpdate);
        app.MapPut("/api/objects", HandleObjectUpdate);
        app.MapGet("/api/objects/labels", HandleObjectLabels);
        app.MapGet("/api/area", HandleArea);
        app.MapPut("/api/area", HandleAreaUpdate);
    }

    //--------------------------------------------------------------------------------
    // Update
    //--------------------------------------------------------------------------------

    private static NoContent HandleDetectionUpdate(DetectionRequest request, FaceDetectionService detection, DetectionGate gate)
    {
        if (request.Enabled is { } enabled)
        {
            detection.IsEnabled = enabled;
        }

        if (request.MotionGate is { } motionGate)
        {
            gate.IsEnabled = motionGate;
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
    // Object
    //--------------------------------------------------------------------------------

    private static NoContent HandleObjectUpdate(ObjectRequest request, ObjectDetectionService objectDetection)
    {
        if (request.Enabled is { } enabled)
        {
            objectDetection.IsEnabled = enabled;
        }

        return TypedResults.NoContent();
    }

    private static Ok<ObjectLabel[]> HandleObjectLabels() =>
        TypedResults.Ok(Labels);

    private static ObjectLabel[] CreateLabels()
    {
        var labels = new ObjectLabel[ObjectLabels.Count];
        for (var i = 0; i < labels.Length; i++)
        {
            var group = ObjectLabels.GetGroup(i) switch
            {
                ObjectGroup.Person => "person",
                ObjectGroup.Animal => "animal",
                ObjectGroup.Vehicle => "vehicle",
                _ => "other"
            };
            labels[i] = new ObjectLabel(ObjectLabels.Names[i], group);
        }

        return labels;
    }

    //--------------------------------------------------------------------------------
    // Area
    //--------------------------------------------------------------------------------

    private static Ok<DetectionAreaData> HandleArea(AreaService area) =>
        TypedResults.Ok(DetectionAreaData.From(area.Current));

    // Applied to the next frame and saved to the file (500 when the file cannot be written, the area is used until the server stops)
    private static Results<NoContent, BadRequest, ProblemHttpResult> HandleAreaUpdate(DetectionAreaData request, AreaService area)
    {
        if (!request.TryGetArea(out var value))
        {
            return TypedResults.BadRequest();
        }

        area.Update(value);
        if (!area.Save())
        {
            return TypedResults.Problem("The area could not be saved to the file.", statusCode: StatusCodes.Status500InternalServerError);
        }

        return TypedResults.NoContent();
    }
}
