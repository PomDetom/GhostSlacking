using System.Drawing;

namespace GhostSlacking.Core;

public sealed record WindowStyleSnapshot(nint Style, nint ExtendedStyle);

public sealed record WindowPlacementSnapshot(
    int Flags,
    int ShowCommand,
    Point MinPosition,
    Point MaxPosition,
    Rectangle NormalPosition,
    Rectangle DevicePosition)
{
    public bool IsMinimized => ShowCommand is 2 or 6 or 7 or 11;
    public bool IsMaximized => ShowCommand == 3;
}

public sealed record WindowSnapshot
{
    public required nint Hwnd { get; init; }
    public required uint ProcessId { get; init; }
    public string? ProcessName { get; init; }
    public required string ProcessStartIdentity { get; init; }
    public required Rectangle ScreenBounds { get; init; }
    public required bool WasVisible { get; init; }
    public required bool WasMinimized { get; init; }
    public required WindowPlacementSnapshot Placement { get; init; }
    public byte[]? OriginalRegionData { get; init; }
    public int? OriginalSystemBackdropType { get; init; }
    public int? OriginalNonClientRenderingPolicy { get; init; }
    public int? OriginalWindowCornerPreference { get; init; }
    public int? OriginalBorderColor { get; init; }
    public required WindowStyleSnapshot Styles { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public int SchemaVersion { get; init; } = 2;
}
