using System.Drawing;

namespace GhostSlacking.Core;

// Runtime geometry is queried separately: it must never replace captured styles,
// identity, visibility or region data with the temporarily modified Ghost state.
public sealed record WindowDisplayContext(
    string Monitor, Rectangle MonitorBounds, Rectangle WorkArea, uint WindowDpi, int ScalePercent);

public sealed record WindowEnvironment(
    WindowDisplayContext Display, Rectangle Bounds, WindowPlacementSnapshot Placement)
{
    public bool IsSystemMaximized { get; init; }
}
