using GhostSlacking.Core;

namespace GhostSlacking.Watchdog;

internal sealed class WatchdogFileLogger : ILogger
{
    private readonly object _gate = new();
    private readonly string _path;

    public WatchdogFileLogger()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GhostSlacking",
            "logs");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "watchdog.log");
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        try
        {
            lock (_gate)
            {
                var suffix = exception is null ? string.Empty : $" | {exception.GetType().Name}: {exception.Message}";
                File.AppendAllText(_path, $"{DateTimeOffset.Now:O} [{level}] {message}{suffix}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never prevent or interrupt a recovery attempt.
        }
    }
}
