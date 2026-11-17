using System.Text.Json;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal sealed record UpdateState(DateTimeOffset? LastSuccessfulCheckUtc = null, string? SkippedVersion = null);

internal sealed class UpdateStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly ILogger _logger;

    public UpdateStateStore(string? path = null, ILogger? logger = null)
    {
        _path = path ?? Path.Combine(GhostSlackingDataPaths.RootDirectory, "update-state.json");
        _logger = logger ?? NullLogger.Instance;
    }

    public UpdateState Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new UpdateState();
            }

            return JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(_path), JsonOptions) ?? new UpdateState();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Log(LogLevel.Warning, "Update state is invalid; using defaults.", exception);
            return new UpdateState();
        }
    }

    public bool Save(UpdateState state)
    {
        var temporaryPath = $"{_path}.{Environment.ProcessId}.tmp";
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Log(LogLevel.Warning, "Unable to save update state.", exception);
            TryDelete(temporaryPath);
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
