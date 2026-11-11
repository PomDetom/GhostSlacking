
namespace GhostSlacking.Core;

public enum LogLevel
{
    Error,
    Warning,
    Info,
    Debug
}

public interface ILogger
{
    void Log(LogLevel level, string message, Exception? exception = null);
}
