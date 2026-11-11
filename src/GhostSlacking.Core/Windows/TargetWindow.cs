using System.Drawing;

namespace GhostSlacking.Core;

public sealed record TargetWindow
{
    public required nint Hwnd { get; init; }
    public required uint ProcessId { get; init; }
    public string? Title { get; init; }
    public string? ProcessName { get; init; }
    public required Rectangle ScreenBounds { get; init; }
}
