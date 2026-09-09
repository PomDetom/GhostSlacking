using GhostSlacking.Core;

namespace GhostSlacking.App;

public sealed class FileLogger : ILogger, IDisposable
{
    private readonly RollingFileLogger _inner;

    public FileLogger(AppSettings settings)
    {
        _inner = new RollingFileLogger(
            Path.Combine(GhostSlackingDataPaths.LogDirectory, "ghostslacking.log"),
            settings.MinimumLogLevel);
    }

    public LogLevel MinimumLevel
    {
        get => _inner.MinimumLevel;
        set => _inner.MinimumLevel = value;
    }

    public void Log(LogLevel level, string message, Exception? exception = null) =>
        _inner.Log(level, message, exception);

    public void Dispose() => _inner.Dispose();
}
