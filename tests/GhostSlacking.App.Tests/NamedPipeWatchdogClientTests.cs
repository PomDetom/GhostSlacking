using System.Diagnostics;
using GhostSlacking.Platform;

namespace GhostSlacking.App.Tests;

public sealed class NamedPipeWatchdogClientTests
{
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
}
