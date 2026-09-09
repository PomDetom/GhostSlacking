using System.Text.Json;

namespace GhostSlacking.Core;

public sealed class SettingsStore
{
    private readonly string _path;
    private readonly ILogger _logger;
    public SettingsStore(string? path = null, ILogger? logger = null)
    {
        _path = path ?? GhostSlackingDataPaths.SettingsFilePath;
        _logger = logger ?? NullLogger.Instance;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            return AppSettingsJson.Deserialize(File.ReadAllText(_path));
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.Log(LogLevel.Warning, "Settings are invalid; using defaults.", exception);
            return new AppSettings();
        }
    }

    public bool Save(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, AppSettingsJson.Serialize(settings));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Log(LogLevel.Error, "Unable to save settings.", exception);
            return false;
        }
    }
}
