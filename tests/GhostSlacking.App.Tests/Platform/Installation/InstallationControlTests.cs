using System.IO.Pipes;
using System.Text;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App.Tests;

public sealed class InstallationControlTests
{
    [Fact]
    public async Task Shutdown_timeout_closes_the_pipe_without_granting_file_replacement()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new InstallationControlServer(directory, () => requested.TrySetResult(),
            NullLogger.Instance, TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = new NamedPipeClientStream(".", InstallationControlServer.PipeName(directory),
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(cancellation.Token);
        using var reader = new StreamReader(client, new UTF8Encoding(false), false, leaveOpen: true);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        Assert.StartsWith("IDENTITY ", await reader.ReadLineAsync(cancellation.Token));
        await writer.WriteLineAsync("CLOSE");
        await requested.Task.WaitAsync(cancellation.Token);
        Assert.Null(await reader.ReadLineAsync(cancellation.Token));
    }

    [Fact]
    public async Task Disconnected_client_does_not_disable_a_later_safe_close_request()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new InstallationControlServer(directory, () => requested.TrySetResult(), NullLogger.Instance);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using (var invalid = new NamedPipeClientStream(".", InstallationControlServer.PipeName(directory),
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            await invalid.ConnectAsync(cancellation.Token);
            using var reader = new StreamReader(invalid, new UTF8Encoding(false), false, leaveOpen: true);
            Assert.StartsWith("IDENTITY ", await reader.ReadLineAsync(cancellation.Token));
        }
        Assert.False(requested.Task.IsCompleted);
        using var valid = new NamedPipeClientStream(".", InstallationControlServer.PipeName(directory),
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await valid.ConnectAsync(cancellation.Token);
        using var response = new StreamReader(valid, new UTF8Encoding(false), false, leaveOpen: true);
        using var writer = new StreamWriter(valid, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        Assert.StartsWith("IDENTITY ", await response.ReadLineAsync(cancellation.Token));
        await writer.WriteLineAsync("CLOSE");
        await requested.Task.WaitAsync(cancellation.Token);
        server.CompleteShutdown(true);
        Assert.Equal("SAFE", await response.ReadLineAsync(cancellation.Token));
    }

    [Theory]
    [InlineData(true, "SAFE")]
    [InlineData(false, "UNSAFE")]
    public async Task Shutdown_endpoint_requires_explicit_restoration_confirmation(bool restored, string expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new InstallationControlServer(directory, () => requested.TrySetResult(), NullLogger.Instance);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = new NamedPipeClientStream(".", InstallationControlServer.PipeName(directory),
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(cancellation.Token);
        using var reader = new StreamReader(client, new UTF8Encoding(false), false, leaveOpen: true);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        Assert.StartsWith($"IDENTITY {Environment.ProcessId} ", await reader.ReadLineAsync(cancellation.Token));
        await writer.WriteLineAsync("CLOSE");
        await requested.Task.WaitAsync(cancellation.Token);
        var reply = reader.ReadLineAsync(cancellation.Token).AsTask();
        Assert.False(reply.IsCompleted);
        server.CompleteShutdown(restored);
        Assert.Equal(expected, await reply);
    }
}
