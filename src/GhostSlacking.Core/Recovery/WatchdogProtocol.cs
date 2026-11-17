using System.Text.Json;
using System.Text.Json.Serialization;

namespace GhostSlacking.Core;

public static class WatchdogProtocol
{
    public const int Version = 1;
    public const int ManifestSchemaVersion = 1;
    public const int SnapshotSchemaVersion = 2;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(WatchdogMessage message) =>
        JsonSerializer.Serialize(message, SerializerOptions);

    public static WatchdogMessage Deserialize(string json) =>
        JsonSerializer.Deserialize<WatchdogMessage>(json, SerializerOptions)
        ?? throw new JsonException("The watchdog message was empty.");
}
