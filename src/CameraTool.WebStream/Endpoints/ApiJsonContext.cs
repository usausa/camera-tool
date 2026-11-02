namespace CameraTool.WebStream.Endpoints;

using System.Text.Json;
using System.Text.Json.Serialization;

using CameraTool.WebStream.Components;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(StreamStatus))]
[JsonSerializable(typeof(DetectionRequest))]
public sealed partial class ApiJsonContext : JsonSerializerContext;
