using GhostSlacking.Core;

namespace GhostSlacking.App;

public sealed class FileLogger : ILogger, IDisposable
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly string _path;

    public FileLogger(AppSettings settings)
    {
        _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GhostSlacking", "logs");
        _path = Path.Combine(_directory, "ghostslacking.log");
        _minimumLevel = settings.MinimumLogLevel;
        Directory.CreateDirectory(_directory);
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (level > _minimumLevel)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                RotateIfNeeded();
                var suffix = exception is null ? string.Empty : $" | {exception.GetType().Name}: {exception.Message}";
                File.AppendAllText(_path, $"{DateTimeOffset.Now:O} [{level}] {message}{suffix}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never compromise the recovery path.
        }
    }

    public void Dispose() { }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_path) || new FileInfo(_path).Length < MaxBytes)
        {
            return;
        }

        var backup = Path.Combine(_directory, "ghostslacking.1.log");
        File.Copy(_path, backup, true);
        File.Delete(_path);
    }
}
