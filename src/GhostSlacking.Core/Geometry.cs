using System.Drawing;

namespace GhostSlacking.Core;

public static class RevealGeometry
{
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

    public static Rectangle IntersectWithWindow(CircleRegion region, Rectangle windowBounds)
    {
        var circle = GetBounds(region);
        return Rectangle.Intersect(circle, new Rectangle(0, 0, windowBounds.Width, windowBounds.Height));
    }

    public static bool IsInside(Rectangle bounds, Point point) => bounds.Contains(point);
}
