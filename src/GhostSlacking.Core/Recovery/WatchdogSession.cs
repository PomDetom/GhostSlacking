
namespace GhostSlacking.Core;

public enum WatchdogMessageDisposition
{
    Accepted,
    RejectedProtocolVersion,
    RejectedSession,
    RejectedSequence,
    RejectedPayload,
    ShutdownCompleted
}

public sealed class WatchdogSession
{
    private readonly Guid _sessionId;
    private readonly TimeSpan _heartbeatTimeout;
    private DateTimeOffset? _lastContactUtc;

    public WatchdogSession(Guid sessionId, TimeSpan heartbeatTimeout)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty watchdog session ID is required.", nameof(sessionId));
        }

        if (heartbeatTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatTimeout));
        }

        _sessionId = sessionId;
        _heartbeatTimeout = heartbeatTimeout;
    }

    public bool HandshakeCompleted { get; private set; }
    public bool IsShutdownCompleted { get; private set; }
    public RecoveryManifest? LastManifest { get; private set; }

    public WatchdogMessageDisposition Receive(WatchdogMessage message, DateTimeOffset receivedAtUtc)
    {
        if (message.ProtocolVersion != WatchdogProtocol.Version)
        {
            return WatchdogMessageDisposition.RejectedProtocolVersion;
        }

        if (message.SessionId != _sessionId)
        {
            return WatchdogMessageDisposition.RejectedSession;
        }

        if (!HandshakeCompleted)
        {
            if (message.Kind != WatchdogMessageKind.Hello ||
                message.MainProcessId is not > 0 ||
                string.IsNullOrWhiteSpace(message.MainProcessStartIdentity))
            {
                return message.Kind == WatchdogMessageKind.Hello
                    ? WatchdogMessageDisposition.RejectedPayload
                    : WatchdogMessageDisposition.RejectedSequence;
            }

            HandshakeCompleted = true;
            _lastContactUtc = receivedAtUtc;
            return WatchdogMessageDisposition.Accepted;
        }

        if (IsShutdownCompleted)
        {
            return WatchdogMessageDisposition.RejectedSequence;
        }

        switch (message.Kind)
        {
            case WatchdogMessageKind.Heartbeat:
                _lastContactUtc = receivedAtUtc;
                return WatchdogMessageDisposition.Accepted;
            case WatchdogMessageKind.RecoveryManifest:
                if (message.Manifest is null ||
                    message.Manifest.SchemaVersion != WatchdogProtocol.ManifestSchemaVersion ||
                    message.Manifest.ManifestId == Guid.Empty ||
                    message.Manifest.SessionId != _sessionId ||
                    message.Manifest.Items is null ||
                    message.Manifest.Items.Any(item =>
                        item.SnapshotSchemaVersion != WatchdogProtocol.SnapshotSchemaVersion ||
                        item.Placement is null))
                {
                    return WatchdogMessageDisposition.RejectedPayload;
                }

                LastManifest = message.Manifest;
                _lastContactUtc = receivedAtUtc;
                return WatchdogMessageDisposition.Accepted;
            case WatchdogMessageKind.ShutdownCompleted:
                LastManifest = null;
                _lastContactUtc = receivedAtUtc;
                IsShutdownCompleted = true;
                return WatchdogMessageDisposition.ShutdownCompleted;
            default:
                return WatchdogMessageDisposition.RejectedSequence;
        }
    }

    public bool HasHeartbeatTimedOut(DateTimeOffset nowUtc) =>
        HandshakeCompleted &&
        !IsShutdownCompleted &&
        _lastContactUtc is DateTimeOffset lastContact &&
        nowUtc - lastContact >= _heartbeatTimeout;

    public RecoveryManifest? TakeTimedOutManifest(DateTimeOffset nowUtc)
    {
        if (!HasHeartbeatTimedOut(nowUtc))
        {
            return null;
        }

        var manifest = LastManifest;
        LastManifest = null;
        return manifest;
    }
}
