
namespace GhostSlacking.Core;

// Kept as CircleRegion for compatibility with the V0.1 core API. The region
// now carries the selected shape and is used for all Reveal geometries.
public readonly record struct CircleRegion(
    int CenterX,
    int CenterY,
    int DiameterPx,
    RevealShape Shape = RevealShape.Circle,
    int CornerRadiusPx = 0)
{
    public int Radius => DiameterPx / 2;
    public int CornerRadius => Math.Clamp(CornerRadiusPx, 0, Radius);

    public override string ToString() => $"shape={Shape}, center=({CenterX},{CenterY}), diameter={DiameterPx}";
}
