using System.Drawing;

namespace GhostSlacking.Core;

public sealed record RevealVisualState(
    nint TargetHwnd,
    Rectangle WindowBounds,
    CircleRegion CoreRegion,
    CircleRegion ContentRegion,
    int FeatherWidthPx,
    float BlurAmountPx);
