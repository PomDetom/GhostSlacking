using System.Drawing;

namespace GhostSlacking.Core;

public sealed record WindowObservation
{
    public required nint Hwnd { get; init; }
    public required uint ProcessId { get; init; }
    public required Rectangle ScreenBounds { get; init; }
    public required bool IsVisible { get; init; }
    public required bool IsMinimized { get; init; }
    public required bool IsMaximized { get; init; }
    public string? ProcessName { get; init; }
}
