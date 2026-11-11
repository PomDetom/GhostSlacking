
namespace GhostSlacking.Core;

public sealed record RecoveryManifest
{
    public int SchemaVersion { get; init; } = WatchdogProtocol.ManifestSchemaVersion;
    public required Guid ManifestId { get; init; }
    public required Guid SessionId { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public required IReadOnlyList<WatchdogRecoveryItem> Items { get; init; }

    public static RecoveryManifest FromProfiles(
        Guid sessionId,
        IEnumerable<GhostWindowProfile> profiles,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty watchdog session ID is required.", nameof(sessionId));
        }

        return new RecoveryManifest
        {
            ManifestId = Guid.NewGuid(),
            SessionId = sessionId,
            UpdatedAtUtc = updatedAtUtc,
            Items = profiles.Select(profile => WatchdogRecoveryItem.FromSnapshot(profile.Original)).ToArray()
        };
    }
}
