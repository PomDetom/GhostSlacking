using System.Drawing;

namespace GhostSlacking.Core;

public static class RevealGeometry
{
    private const float ContentExtentRatio = 0.7F;
    public static CircleRegion CreateCircle(Rectangle windowBounds, Point cursorScreen, int diameterPx)
    {
        if (diameterPx is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(diameterPx));
        }

        var localX = cursorScreen.X - windowBounds.Left;
        var localY = cursorScreen.Y - windowBounds.Top;
        return new CircleRegion(localX, localY, diameterPx);
    }

    public static CircleRegion CreateReveal(Rectangle windowBounds, Point cursorScreen, RevealSettings settings)
    {
        if (!settings.IsValid())
        {
            throw new ArgumentException("Reveal settings are invalid.", nameof(settings));
        }

        var localX = cursorScreen.X - windowBounds.Left;
        var localY = cursorScreen.Y - windowBounds.Top;
        var cornerRadius = settings.Shape == RevealShape.RoundedRectangle
            ? Math.Clamp(settings.DiameterPx / 5, 12, 48)
            : 0;

        return new CircleRegion(localX, localY, settings.DiameterPx, settings.Shape, cornerRadius);
    }

    public static Rectangle GetBounds(CircleRegion region)
    {
        var radius = region.DiameterPx / 2;
        return new Rectangle(region.CenterX - radius, region.CenterY - radius, region.DiameterPx, region.DiameterPx);
    }

    public static CircleRegion Expand(CircleRegion region, int extentPx)
    {
        if (extentPx < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(extentPx));
        }

        return region with
        {
            DiameterPx = checked(region.DiameterPx + (extentPx * 2)),
            CornerRadiusPx = region.Shape == RevealShape.RoundedRectangle
                ? checked(region.CornerRadius + extentPx)
                : 0
        };
    }

    public static RevealVisualState CreateVisualState(
        nint targetHwnd,
        Rectangle windowBounds,
        CircleRegion coreRegion,
        int featherWidthPx,
        RevealBlurLevel blurLevel)
    {
        if (featherWidthPx is < 0 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(featherWidthPx));
        }

        if (blurLevel is not (RevealBlurLevel.Low or RevealBlurLevel.Medium or RevealBlurLevel.High))
        {
            throw new ArgumentOutOfRangeException(nameof(blurLevel));
        }

        var contentExtent = (int)MathF.Ceiling(featherWidthPx * ContentExtentRatio);
        var maximumBlurAmount = featherWidthPx == 0
            ? 0F
            : Math.Clamp(featherWidthPx / 3F, 2F, 24F);
        var blurRatio = blurLevel switch
        {
            RevealBlurLevel.Low => 0.2F,
            RevealBlurLevel.Medium => 0.55F,
            RevealBlurLevel.High => 1F,
            _ => throw new ArgumentOutOfRangeException(nameof(blurLevel))
        };
        var blurAmount = maximumBlurAmount == 0F
            ? 0F
            : Math.Max(0.5F, maximumBlurAmount * blurRatio);
        return new RevealVisualState(
            targetHwnd,
            windowBounds,
            coreRegion,
            Expand(coreRegion, contentExtent),
            featherWidthPx,
            blurAmount);
    }

    public static Rectangle GetFeatherBounds(RevealVisualState visual)
    {
        var localBounds = GetBounds(Expand(visual.CoreRegion, visual.FeatherWidthPx));
        localBounds.Offset(visual.WindowBounds.Location);
        return Rectangle.Intersect(localBounds, visual.WindowBounds);
    }

    public static float SignedDistanceFromBoundary(CircleRegion region, float localX, float localY)
    {
        var halfWidth = region.DiameterPx / 2F;
        var halfHeight = region.DiameterPx / 2F;
        var x = localX - region.CenterX;
        var y = localY - region.CenterY;
        if (region.Shape == RevealShape.Circle)
        {
            return MathF.Sqrt((x * x) + (y * y)) - halfWidth;
        }

        var radius = region.Shape == RevealShape.RoundedRectangle ? region.CornerRadius : 0F;
        var qx = MathF.Abs(x) - (halfWidth - radius);
        var qy = MathF.Abs(y) - (halfHeight - radius);
        var outside = MathF.Sqrt(
            (MathF.Max(qx, 0F) * MathF.Max(qx, 0F)) +
            (MathF.Max(qy, 0F) * MathF.Max(qy, 0F)));
        return outside + MathF.Min(MathF.Max(qx, qy), 0F) - radius;
    }

    public static float GetFeatherOpacity(float outwardDistancePx, int featherWidthPx)
    {
        if (featherWidthPx <= 0 || outwardDistancePx <= 0F || outwardDistancePx >= featherWidthPx)
        {
            return 0F;
        }

        var p = outwardDistancePx / featherWidthPx;
        if (p < 0.15F)
        {
            return SmoothStep(p / 0.15F);
        }

        if (p <= ContentExtentRatio)
        {
            return 1F;
        }

        return 1F - SmoothStep((p - ContentExtentRatio) / (1F - ContentExtentRatio));
    }

    public static Rectangle IntersectWithWindow(CircleRegion region, Rectangle windowBounds)
    {
        var circle = GetBounds(region);
        return Rectangle.Intersect(circle, new Rectangle(0, 0, windowBounds.Width, windowBounds.Height));
    }

    public static bool IsInside(Rectangle bounds, Point point) => bounds.Contains(point);

    private static float SmoothStep(float value)
    {
        var t = Math.Clamp(value, 0F, 1F);
        return t * t * (3F - (2F * t));
    }
}
