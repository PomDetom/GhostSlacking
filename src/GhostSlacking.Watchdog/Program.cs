using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.Watchdog;

internal static class Program
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TimeoutPollInterval = TimeSpan.FromMilliseconds(250);

    private static async Task<int> Main(string[] args)
    {
        var logger = new WatchdogFileLogger();
        if (!TryParseArguments(args, out var options, out var argumentError))
        {
            logger.Log(LogLevel.Error, $"Watchdog startup rejected: {argumentError}");
            return 2;
        }

        try
        {
            return await RunAsync(options, logger).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            logger.Log(LogLevel.Error, "Watchdog terminated before recovery could complete.", exception);
            return 3;
        }
    }

    private static async Task<int> RunAsync(WatchdogOptions options, ILogger logger)
    {
        using var pipe = new NamedPipeServerStream(
            options.PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var connectionCancellation = new CancellationTokenSource(ConnectionTimeout);
        await pipe.WaitForConnectionAsync(connectionCancellation.Token).ConfigureAwait(false);

        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        var session = new WatchdogSession(options.SessionId, options.HeartbeatTimeout);
        var firstLine = await reader.ReadLineAsync(connectionCancellation.Token).ConfigureAwait(false);
        if (!TryReceive(firstLine, session, logger, out var firstDisposition) ||
            firstDisposition != WatchdogMessageDisposition.Accepted ||
            !session.HandshakeCompleted)
        {
            logger.Log(LogLevel.Error, $"Watchdog handshake rejected disposition={firstDisposition}");
            return 4;
        }

        var acknowledgment = new WatchdogMessage
        {
            SessionId = options.SessionId,
            Kind = WatchdogMessageKind.HelloAcknowledged,
            SentAtUtc = DateTimeOffset.UtcNow
        };
        await writer.WriteLineAsync(WatchdogProtocol.Serialize(acknowledgment)).ConfigureAwait(false);
        logger.Log(LogLevel.Info, $"WatchdogSessionStarted session={options.SessionId}");

        Task<string?>? pendingRead = ReadLineOrDisconnectAsync(reader, logger);
        while (!session.IsShutdownCompleted)
        {
            var poll = Task.Delay(TimeoutPollInterval);
            if (pendingRead is not null)
            {
                var completed = await Task.WhenAny(pendingRead, poll).ConfigureAwait(false);
                if (completed == pendingRead)
                {
                    var line = await pendingRead.ConfigureAwait(false);
                    if (line is null)
                    {
                        pendingRead = null;
                        logger.Log(LogLevel.Warning, $"WatchdogPipeDisconnected session={options.SessionId}");
                    }
                    else
                    {
                        if (!TryReceive(line, session, logger, out var disposition))
                        {
                            pendingRead = null;
                            logger.Log(LogLevel.Warning, "Watchdog stopped reading after a malformed message; the last valid manifest remains eligible for timeout recovery.");
                            continue;
                        }

                        if (disposition == WatchdogMessageDisposition.ShutdownCompleted)
                        {
                            logger.Log(LogLevel.Info, $"WatchdogCleanShutdown session={options.SessionId}");
                            return 0;
                        }

                        if (disposition != WatchdogMessageDisposition.Accepted)
                        {
                            logger.Log(LogLevel.Error, $"Watchdog message rejected disposition={disposition}");
                            pendingRead = null;
                            continue;
                        }

                        pendingRead = ReadLineOrDisconnectAsync(reader, logger);
                    }
                }
            }
            else
            {
                await poll.ConfigureAwait(false);
            }

            var now = DateTimeOffset.UtcNow;
            if (!session.HasHeartbeatTimedOut(now))
            {
                continue;
            }

            var manifest = session.TakeTimedOutManifest(now);
            logger.Log(LogLevel.Warning, $"WatchdogHeartbeatTimeout session={options.SessionId}");
            if (manifest is null || manifest.Items.Count == 0)
            {
                logger.Log(LogLevel.Info, "Watchdog recovery manifest was empty; no targets were modified.");
                return 0;
            }

            var executor = new WatchdogRecoveryExecutor(new Win32WatchdogRecoveryTarget(logger));
            var report = executor.Execute(manifest);
            foreach (var item in report.Items)
            {
                var level = item.Success ? LogLevel.Info : item.Skipped ? LogLevel.Warning : LogLevel.Error;
                logger.Log(level,
                    $"WatchdogRecovery hwnd={item.Hwnd} success={item.Success} skipped={item.Skipped} error={item.ErrorCode} reason={item.Reason}");
            }

            logger.Log(LogLevel.Info, $"WatchdogManifestCleared manifest={manifest.ManifestId}");
            return report.HasFailures ? 6 : 0;
        }

        return 0;
    }

    private static bool TryReceive(
        string? line,
        WatchdogSession session,
        ILogger logger,
        out WatchdogMessageDisposition disposition)
    {
        disposition = WatchdogMessageDisposition.RejectedPayload;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            var message = WatchdogProtocol.Deserialize(line);
            disposition = session.Receive(message, DateTimeOffset.UtcNow);
            return true;
        }
        catch (JsonException exception)
        {
            logger.Log(LogLevel.Error, "Watchdog received malformed JSON.", exception);
            return false;
        }
    }

    private static async Task<string?> ReadLineOrDisconnectAsync(StreamReader reader, ILogger logger)
    {
        try
        {
            return await reader.ReadLineAsync().ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            logger.Log(LogLevel.Warning, "Watchdog pipe read failed; waiting for the heartbeat timeout.", exception);
            return null;
        }
    }

    private static bool TryParseArguments(string[] args, out WatchdogOptions options, out string error)
    {
        options = default;
        error = string.Empty;
        string? pipeName = null;
        Guid sessionId = Guid.Empty;
        var heartbeatTimeout = TimeSpan.Zero;

        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
            {
                error = "Each watchdog option must have a value.";
                return false;
            }

            switch (args[index])
            {
                case "--pipe":
                    pipeName = args[index + 1];
                    break;
                case "--session" when Guid.TryParse(args[index + 1], out var parsedSession):
                    sessionId = parsedSession;
                    break;
                case "--heartbeat-timeout-ms" when int.TryParse(
                    args[index + 1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var timeoutMilliseconds):
                    heartbeatTimeout = TimeSpan.FromMilliseconds(timeoutMilliseconds);
                    break;
                default:
                    error = $"Unknown or invalid watchdog option: {args[index]}";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(pipeName) || sessionId == Guid.Empty || heartbeatTimeout <= TimeSpan.Zero)
        {
            error = "Pipe name, session ID, and a positive heartbeat timeout are required.";
            return false;
        }

        options = new WatchdogOptions(pipeName, sessionId, heartbeatTimeout);
        return true;
    }

    private readonly record struct WatchdogOptions(string PipeName, Guid SessionId, TimeSpan HeartbeatTimeout);
}
