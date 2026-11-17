using System.Text.Json;
using System.Text.Json.Serialization;

namespace GhostSlacking.Core;

public static class AppSettingsJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(AppSettings settings) =>
        JsonSerializer.Serialize(settings.Normalize(), Options);

    public static AppSettings Deserialize(string json) =>
        (JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings()).Normalize();
}
