namespace CameraTool.WebStream.Endpoints;

using System.Text.Json;
using System.Text.Json.Serialization;

using CameraTool.Vision;
using CameraTool.WebStream.Services;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(StreamStatus))]
[JsonSerializable(typeof(DetectionRequest))]
[JsonSerializable(typeof(MotionRequest))]
[JsonSerializable(typeof(ObjectRequest))]
[JsonSerializable(typeof(ObjectLabel[]))]
[JsonSerializable(typeof(DetectionAreaData))]
public sealed partial class ApiJsonContext : JsonSerializerContext;
