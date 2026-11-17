
namespace GhostSlacking.Core;

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
