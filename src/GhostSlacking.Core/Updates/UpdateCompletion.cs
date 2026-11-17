using System.Text.Json;

namespace GhostSlacking.Core;

public enum UpdateCompletionStatus
{
    Succeeded,
    Cancelled,
    Failed,
    LaunchFailed
}

public sealed record UpdateCompletionResult(
    string Version,
    UpdateCompletionStatus Status,
    int? InstallerExitCode,
    DateTimeOffset CompletedAtUtc,
    string? FailureStage = null);

public sealed class UpdateCompletionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly ILogger _logger;

    public UpdateCompletionStore(string? path = null, ILogger? logger = null)
    {
        _path = path ?? Path.Combine(GhostSlackingDataPaths.RootDirectory, "update-completion.json");
        _logger = logger ?? NullLogger.Instance;
    }

    public bool Save(UpdateCompletionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var temporaryPath = $"{_path}.{Environment.ProcessId}.tmp";
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(result, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Log(LogLevel.Warning, "Unable to save the update completion result.", exception);
            TryDelete(temporaryPath);
            return false;
        }
    }

    public UpdateCompletionResult? Consume()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize<UpdateCompletionResult>(File.ReadAllText(_path), JsonOptions);
            if (result is null || !IsValidVersion(result.Version) || !Enum.IsDefined(result.Status))
            {
                throw new InvalidDataException("The update completion result is invalid.");
            }

            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            _logger.Log(LogLevel.Warning, "The update completion result could not be read.", exception);
            return null;
        }
        finally
        {
            TryDelete(_path);
        }
    }

    private static bool IsValidVersion(string versionText) => ReleaseVersion.TryParse(versionText, out _);

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
