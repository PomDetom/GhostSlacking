
namespace GhostSlacking.Core;

public sealed class NullLogger : ILogger
{
    public static readonly NullLogger Instance = new();
    private NullLogger() { }
    public void Log(LogLevel level, string message, Exception? exception = null) { }
}
