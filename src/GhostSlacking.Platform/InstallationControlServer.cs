using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

public sealed class InstallationControlServer : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource<bool> _shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _worker;
    private readonly ILogger _logger;
    private volatile bool _closeRequested;

    public InstallationControlServer(string directory, Action requestClose, ILogger logger)
        : this(directory, requestClose, logger, TimeSpan.FromSeconds(30)) { }

    internal InstallationControlServer(string directory, Action requestClose, ILogger logger, TimeSpan requestDeadline)
    {
        _logger = logger;
        _worker = Task.Run(async () =>
        {
            while (!_cancellation.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(PipeName(directory), PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_cancellation.Token);
                    using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
                    requestTimeout.CancelAfter(requestDeadline);
                    using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, leaveOpen: true);
                    using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                    using var process = Process.GetCurrentProcess();
                    await writer.WriteLineAsync($"IDENTITY {process.Id} {process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)}");
                    if (await reader.ReadLineAsync(requestTimeout.Token) != "CLOSE") continue;
                    _closeRequested = true;
                    requestClose();
                    var safe = await _shutdown.Task.WaitAsync(requestTimeout.Token);
                    await writer.WriteLineAsync(safe ? "SAFE" : "UNSAFE");
                    return;
                }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
                {
                    _logger.Log(LogLevel.Warning, "Installation close handshake ended without authorization.", exception);
                    if (_closeRequested) return;
                }
            }
        });
    }

    public static string PipeName(string directory) => "GhostSlacking.Install." + Convert.ToHexString(
        SHA256.HashData(Encoding.Unicode.GetBytes(Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(directory)).ToUpperInvariant())));

    public void CompleteShutdown(bool safe)
    {
        _shutdown.TrySetResult(safe);
        // Flush the response before the process closes. A disconnected client grants no authorization.
        if (_closeRequested) _worker.Wait(TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _shutdown.TrySetResult(false);
    }
}
