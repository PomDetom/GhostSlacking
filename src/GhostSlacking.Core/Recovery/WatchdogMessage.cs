
namespace GhostSlacking.Core;

public enum WatchdogMessageKind
{
    Hello,
    HelloAcknowledged,
    Heartbeat,
    RecoveryManifest,
    ShutdownCompleted
}

public sealed record WatchdogMessage
{
    public int ProtocolVersion { get; init; } = WatchdogProtocol.Version;
    public required Guid SessionId { get; init; }
    public required WatchdogMessageKind Kind { get; init; }
    public required DateTimeOffset SentAtUtc { get; init; }
    public int? MainProcessId { get; init; }
    public string? MainProcessStartIdentity { get; init; }
    public RecoveryManifest? Manifest { get; init; }
}
