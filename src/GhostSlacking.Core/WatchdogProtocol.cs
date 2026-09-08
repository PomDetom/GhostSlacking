using System.Text.Json;
using System.Text.Json.Serialization;

namespace GhostSlacking.Core;

public static class WatchdogProtocol
{
    public const int Version = 1;
    public const int ManifestSchemaVersion = 1;
    public const int SnapshotSchemaVersion = 2;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(WatchdogMessage message) =>
        JsonSerializer.Serialize(message, SerializerOptions);

    public static WatchdogMessage Deserialize(string json) =>
        JsonSerializer.Deserialize<WatchdogMessage>(json, SerializerOptions)
        ?? throw new JsonException("The watchdog message was empty.");
}

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

public sealed record WatchdogRecoveryItem
{
    public int SnapshotSchemaVersion { get; init; } = WatchdogProtocol.SnapshotSchemaVersion;
    public required long Hwnd { get; init; }
    public required uint ProcessId { get; init; }
    public required string ProcessStartIdentity { get; init; }
    public string? ProcessName { get; init; }
    public required int BoundsX { get; init; }
    public required int BoundsY { get; init; }
    public required int BoundsWidth { get; init; }
    public required int BoundsHeight { get; init; }
    public required bool WasVisible { get; init; }
    public required bool WasMinimized { get; init; }
    public required WindowPlacementSnapshot Placement { get; init; }
    public byte[]? OriginalRegionData { get; init; }
    public required long Style { get; init; }
    public required long ExtendedStyle { get; init; }
    public int? OriginalSystemBackdropType { get; init; }
    public int? OriginalNonClientRenderingPolicy { get; init; }
    public int? OriginalWindowCornerPreference { get; init; }
    public int? OriginalBorderColor { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }

    public static WatchdogRecoveryItem FromSnapshot(WindowSnapshot snapshot) => new()
    {
        SnapshotSchemaVersion = snapshot.SchemaVersion,
        Hwnd = snapshot.Hwnd.ToInt64(),
        ProcessId = snapshot.ProcessId,
        ProcessStartIdentity = snapshot.ProcessStartIdentity,
        ProcessName = snapshot.ProcessName,
        BoundsX = snapshot.ScreenBounds.X,
        BoundsY = snapshot.ScreenBounds.Y,
        BoundsWidth = snapshot.ScreenBounds.Width,
        BoundsHeight = snapshot.ScreenBounds.Height,
        WasVisible = snapshot.WasVisible,
        WasMinimized = snapshot.WasMinimized,
        Placement = snapshot.Placement,
        OriginalRegionData = snapshot.OriginalRegionData,
        Style = snapshot.Styles.Style.ToInt64(),
        ExtendedStyle = snapshot.Styles.ExtendedStyle.ToInt64(),
        OriginalSystemBackdropType = snapshot.OriginalSystemBackdropType,
        OriginalNonClientRenderingPolicy = snapshot.OriginalNonClientRenderingPolicy,
        OriginalWindowCornerPreference = snapshot.OriginalWindowCornerPreference,
        OriginalBorderColor = snapshot.OriginalBorderColor,
        CapturedAt = snapshot.CapturedAt
    };

    public WindowSnapshot ToSnapshot() => new()
    {
        SchemaVersion = SnapshotSchemaVersion,
        Hwnd = (nint)Hwnd,
        ProcessId = ProcessId,
        ProcessStartIdentity = ProcessStartIdentity,
        ProcessName = ProcessName,
        ScreenBounds = new System.Drawing.Rectangle(BoundsX, BoundsY, BoundsWidth, BoundsHeight),
        WasVisible = WasVisible,
        WasMinimized = WasMinimized,
        Placement = Placement,
        OriginalRegionData = OriginalRegionData,
        Styles = new WindowStyleSnapshot((nint)Style, (nint)ExtendedStyle),
        OriginalSystemBackdropType = OriginalSystemBackdropType,
        OriginalNonClientRenderingPolicy = OriginalNonClientRenderingPolicy,
        OriginalWindowCornerPreference = OriginalWindowCornerPreference,
        OriginalBorderColor = OriginalBorderColor,
        CapturedAt = CapturedAt
    };
}

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

public sealed record WatchdogTargetIdentity(long Hwnd, uint ProcessId, string? ProcessStartIdentity);

public interface IWatchdogRecoveryTarget
{
    WatchdogTargetIdentity? Observe(long hwnd);
    NativeResult Restore(WatchdogRecoveryItem item);
}

public sealed class WatchdogRecoveryExecutor
{
    private readonly IWatchdogRecoveryTarget _target;
    private readonly HashSet<(Guid SessionId, Guid ManifestId)> _processed = new();

    public WatchdogRecoveryExecutor(IWatchdogRecoveryTarget target)
    {
        _target = target;
    }

    public RestoreReport Execute(RecoveryManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!_processed.Add((manifest.SessionId, manifest.ManifestId)))
        {
            return new RestoreReport(manifest.Items
                .Select(item => new RestoreItemResult((nint)item.Hwnd, true, true, "Manifest was already processed."))
                .ToArray());
        }

        if (manifest.SchemaVersion != WatchdogProtocol.ManifestSchemaVersion)
        {
            return SkipAll(manifest, "Unsupported recovery manifest version.");
        }

        var results = new List<RestoreItemResult>(manifest.Items.Count);
        foreach (var item in manifest.Items)
        {
            results.Add(RestoreOne(item));
        }

        return new RestoreReport(results);
    }

    private RestoreItemResult RestoreOne(WatchdogRecoveryItem item)
    {
        var hwnd = (nint)item.Hwnd;
        if (item.SnapshotSchemaVersion != WatchdogProtocol.SnapshotSchemaVersion)
        {
            return new RestoreItemResult(hwnd, false, true, "Unsupported recovery snapshot version.");
        }

        if (item.Hwnd == 0 || item.ProcessId == 0 || string.IsNullOrWhiteSpace(item.ProcessStartIdentity))
        {
            return new RestoreItemResult(hwnd, false, true, "Recovery identity is incomplete.");
        }

        var observed = _target.Observe(item.Hwnd);
        if (observed is null)
        {
            return new RestoreItemResult(hwnd, true, true, "Target window no longer exists.");
        }

        if (observed.Hwnd != item.Hwnd)
        {
            return new RestoreItemResult(hwnd, false, true, "HWND identity mismatch.");
        }

        if (observed.ProcessId != item.ProcessId)
        {
            return new RestoreItemResult(hwnd, false, true, "Process ID mismatch; HWND may have been reused.");
        }

        if (observed.ProcessStartIdentity is null ||
            !string.Equals(observed.ProcessStartIdentity, item.ProcessStartIdentity, StringComparison.Ordinal))
        {
            return new RestoreItemResult(hwnd, false, true, "Process start identity mismatch; PID may have been reused.");
        }

        var restored = _target.Restore(item);
        return restored.Success
            ? new RestoreItemResult(hwnd, true, false, "Restored by watchdog.")
            : new RestoreItemResult(hwnd, false, false, restored.ErrorMessage ?? restored.Operation, restored.ErrorCode);
    }

    private static RestoreReport SkipAll(RecoveryManifest manifest, string reason) =>
        new(manifest.Items
            .Select(item => new RestoreItemResult((nint)item.Hwnd, false, true, reason))
            .ToArray());
}

public interface IWatchdogClient : IDisposable
{
    Guid SessionId { get; }
    bool IsConnected { get; }
    void Start();
    void PublishManifest(RecoveryManifest manifest);
    void CompleteShutdown();
}
