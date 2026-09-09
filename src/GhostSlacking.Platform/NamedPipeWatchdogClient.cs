using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

public sealed class NamedPipeWatchdogClient : IWatchdogClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WatchdogHeartbeatTimeout = TimeSpan.FromSeconds(5);

    private readonly object _sendGate = new();
    private readonly string _watchdogExecutablePath;
    private readonly ILogger _logger;
    private readonly IClock _clock;
    private readonly CancellationTokenSource _heartbeatCancellation = new();
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Task? _heartbeatTask;
    private Process? _watchdogProcess;
    private bool _watchdogExitWaitAttempted;
    private bool _started;
    private bool _disposed;

    public NamedPipeWatchdogClient(
        string watchdogExecutablePath,
        ILogger? logger = null,
        IClock? clock = null,
        Guid? sessionId = null)
    {
        _watchdogExecutablePath = watchdogExecutablePath;
        _logger = logger ?? NullLogger.Instance;
        _clock = clock ?? SystemClock.Instance;
        SessionId = sessionId ?? Guid.NewGuid();
        if (SessionId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty watchdog session ID is required.", nameof(sessionId));
        }
    }

    public Guid SessionId { get; }
    public bool IsConnected { get; private set; }
    internal int? WatchdogProcessId => _watchdogProcess?.Id;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _started = true;
        if (!File.Exists(_watchdogExecutablePath))
        {
            throw new FileNotFoundException("The GhostSlacking watchdog executable was not found.", _watchdogExecutablePath);
        }

        var pipeName = $"GhostSlacking.Watchdog.{Environment.ProcessId}.{SessionId:N}";
        var startInfo = new ProcessStartInfo
        {
            FileName = _watchdogExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--session");
        startInfo.ArgumentList.Add(SessionId.ToString("D"));
        startInfo.ArgumentList.Add("--heartbeat-timeout-ms");
        startInfo.ArgumentList.Add(((int)WatchdogHeartbeatTimeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));

        _watchdogProcess = Process.Start(startInfo) ??
            throw new InvalidOperationException("The GhostSlacking watchdog process could not be started.");

        _pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        using var connectCancellation = new CancellationTokenSource(ConnectTimeout);
        _pipe.ConnectAsync(connectCancellation.Token).GetAwaiter().GetResult();
        _reader = new StreamReader(_pipe, new UTF8Encoding(false), false, leaveOpen: true);
        _writer = new StreamWriter(_pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        using var process = Process.GetCurrentProcess();
        var hello = new WatchdogMessage
        {
            SessionId = SessionId,
            Kind = WatchdogMessageKind.Hello,
            SentAtUtc = _clock.UtcNow,
            MainProcessId = Environment.ProcessId,
            MainProcessStartIdentity = process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)
        };
        SendMessage(hello);

        using var acknowledgmentCancellation = new CancellationTokenSource(ConnectTimeout);
        var acknowledgmentLine = _reader.ReadLineAsync(acknowledgmentCancellation.Token).AsTask().GetAwaiter().GetResult();
        if (acknowledgmentLine is null)
        {
            throw new IOException("The watchdog closed the pipe before acknowledging the handshake.");
        }

        var acknowledgment = WatchdogProtocol.Deserialize(acknowledgmentLine);
        if (acknowledgment.ProtocolVersion != WatchdogProtocol.Version ||
            acknowledgment.SessionId != SessionId ||
            acknowledgment.Kind != WatchdogMessageKind.HelloAcknowledged)
        {
            throw new InvalidDataException("The watchdog returned an incompatible handshake acknowledgment.");
        }

        IsConnected = true;
        _heartbeatTask = Task.Run(SendHeartbeatsAsync);
        _logger.Log(LogLevel.Info, $"WatchdogConnected session={SessionId}");
    }

    public void PublishManifest(RecoveryManifest manifest)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SessionId != SessionId)
        {
            throw new ArgumentException("The recovery manifest belongs to a different watchdog session.", nameof(manifest));
        }

        if (!IsConnected)
        {
            return;
        }

        SendMessage(new WatchdogMessage
        {
            SessionId = SessionId,
            Kind = WatchdogMessageKind.RecoveryManifest,
            SentAtUtc = _clock.UtcNow,
            Manifest = manifest
        });
    }

    public void CompleteShutdown()
    {
        if (_disposed || !IsConnected)
        {
            return;
        }

        try
        {
            SendMessage(new WatchdogMessage
            {
                SessionId = SessionId,
                Kind = WatchdogMessageKind.ShutdownCompleted,
                SentAtUtc = _clock.UtcNow
            });
            _logger.Log(LogLevel.Info, $"WatchdogShutdownCompleted session={SessionId}");
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            _logger.Log(LogLevel.Error, "Watchdog clean-shutdown message failed; the timeout recovery path remains active.", exception);
        }
        finally
        {
            IsConnected = false;
            _heartbeatCancellation.Cancel();
            WaitForHeartbeatTask();
            WaitForWatchdogExit(TimeSpan.FromSeconds(2));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IsConnected = false;
        _heartbeatCancellation.Cancel();
        WaitForHeartbeatTask();
        _writer?.Dispose();
        _reader?.Dispose();
        _pipe?.Dispose();
        WaitForWatchdogExit(TimeSpan.FromSeconds(7));
        _watchdogProcess?.Dispose();
        _heartbeatCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task SendHeartbeatsAsync()
    {
        try
        {
            while (!_heartbeatCancellation.IsCancellationRequested)
            {
                await Task.Delay(HeartbeatInterval, _heartbeatCancellation.Token).ConfigureAwait(false);
                SendMessage(new WatchdogMessage
                {
                    SessionId = SessionId,
                    Kind = WatchdogMessageKind.Heartbeat,
                    SentAtUtc = _clock.UtcNow
                });
            }
        }
        catch (OperationCanceledException) when (_heartbeatCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            IsConnected = false;
            _logger.Log(LogLevel.Error, "Watchdog heartbeat failed.", exception);
        }
    }

    private void SendMessage(WatchdogMessage message)
    {
        lock (_sendGate)
        {
            if (_writer is null)
            {
                throw new InvalidOperationException("The watchdog pipe is not connected.");
            }

            _writer.WriteLine(WatchdogProtocol.Serialize(message));
        }
    }

    private void WaitForHeartbeatTask()
    {
        try
        {
            _heartbeatTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
        }
    }

    private void WaitForWatchdogExit(TimeSpan timeout)
    {
        if (_watchdogProcess is null || _watchdogExitWaitAttempted)
        {
            return;
        }

        _watchdogExitWaitAttempted = true;
        try
        {
            if (!_watchdogProcess.WaitForExit(timeout))
            {
                _logger.Log(LogLevel.Error, $"Watchdog did not exit within {timeout.TotalSeconds:0} seconds after the main process began shutdown.");
            }
        }
        catch (InvalidOperationException exception)
        {
            _logger.Log(LogLevel.Error, "Watchdog exit could not be observed.", exception);
        }
    }
}
