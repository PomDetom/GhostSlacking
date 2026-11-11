using System.Diagnostics;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App.Tests;

public sealed class NamedPipeWatchdogClientTests
{
    [Fact]
    public void Watchdog_connection_cancellation_is_logged_and_allows_startup_to_continue()
    {
        var exception = new OperationCanceledException("The watchdog pipe connection timed out.");
        var client = new TestWatchdogClient(exception);
        var logger = new RecordingLogger();

        var result = GhostApplicationController.TryStartWatchdog(client, logger);

        Assert.Null(result);
        Assert.True(client.Disposed);
        Assert.Contains("Watchdog could not be started; crash recovery is unavailable.", logger.Messages);
        Assert.Same(exception, logger.LastException);
    }

    [Fact]
    public void Watchdog_startup_returns_connected_client()
    {
        var client = new TestWatchdogClient();
        var logger = new RecordingLogger();

        var result = GhostApplicationController.TryStartWatchdog(client, logger);

        Assert.Same(client, result);
        Assert.True(client.IsConnected);
        Assert.False(client.Disposed);
    }

    [Fact]
    public void Complete_shutdown_waits_for_watchdog_process_to_exit()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "GhostSlacking.Watchdog.exe");
        using var client = new NamedPipeWatchdogClient(executable);
        client.Start();
        var processId = Assert.IsType<int>(client.WatchdogProcessId);
        using var process = Process.GetProcessById(processId);

        client.CompleteShutdown();

        Assert.True(process.HasExited);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public void Broken_watchdog_pipe_does_not_interrupt_client_disposal()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "GhostSlacking.Watchdog.exe");
        using var client = new NamedPipeWatchdogClient(executable);
        client.Start();
        var processId = Assert.IsType<int>(client.WatchdogProcessId);
        using var process = Process.GetProcessById(processId);
        process.Kill();
        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(5)));

        client.CompleteShutdown();
        client.Dispose();
        client.Dispose();

        Assert.False(client.IsConnected);
    }

    private sealed class TestWatchdogClient(Exception? startException = null) : IWatchdogClient
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public bool IsConnected { get; private set; }
        public bool Disposed { get; private set; }

        public void Start()
        {
            if (startException is not null)
            {
                throw startException;
            }

            IsConnected = true;
        }

        public void PublishManifest(RecoveryManifest manifest) { }
        public void CompleteShutdown() { }
        public void Dispose() => Disposed = true;
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public Exception? LastException { get; private set; }

        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            Messages.Add(message);
            LastException = exception;
        }
    }
}
