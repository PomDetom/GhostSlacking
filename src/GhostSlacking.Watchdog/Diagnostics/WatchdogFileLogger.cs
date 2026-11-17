using GhostSlacking.Core;

namespace GhostSlacking.Watchdog;

internal sealed class WatchdogFileLogger : ILogger
{
    private readonly RollingFileLogger _inner;

    public WatchdogFileLogger()
    {
        _inner = new RollingFileLogger(
            Path.Combine(GhostSlackingDataPaths.LogDirectory, "watchdog.log"),
            LogLevel.Info);
    }

    public void Log(LogLevel level, string message, Exception? exception = null) =>
        _inner.Log(level, message, exception);
}
