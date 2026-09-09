using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal sealed record RevealMaskKey(
    Size SurfaceSize,
    int CoreDiameterPx,
    RevealShape Shape,
    int CornerRadiusPx,
    int FeatherWidthPx,
    float BlurAmountPx);

internal sealed record RevealOverlayLayout(
    Rectangle HostBounds,
    Size SurfaceSize,
    Point VisualOffset,
    CircleRegion TemplateCoreRegion,
    CircleRegion RingOuterRegion,
    CircleRegion RingInnerRegion,
    RevealMaskKey MaskKey)
{
    public static RevealOverlayLayout Create(RevealVisualState visual)
    {
        var blurPadding = (int)MathF.Ceiling(visual.BlurAmountPx * 3F);
        var surfaceExtent = checked(visual.FeatherWidthPx + blurPadding);
        var surfaceBounds = RevealGeometry.GetBounds(
            RevealGeometry.Expand(visual.CoreRegion, surfaceExtent));
        var templateCore = visual.CoreRegion with
        {
            CenterX = visual.CoreRegion.CenterX - surfaceBounds.Left,
            CenterY = visual.CoreRegion.CenterY - surfaceBounds.Top
        };
        var surfaceSize = surfaceBounds.Size;
        return new RevealOverlayLayout(
            visual.WindowBounds,
            surfaceSize,
            surfaceBounds.Location,
            templateCore,
            RevealGeometry.Expand(visual.CoreRegion, visual.FeatherWidthPx),
            visual.CoreRegion,
            new RevealMaskKey(
                surfaceSize,
                visual.CoreRegion.DiameterPx,
                visual.CoreRegion.Shape,
                visual.CoreRegion.CornerRadius,
                visual.FeatherWidthPx,
                visual.BlurAmountPx));
    }
}
