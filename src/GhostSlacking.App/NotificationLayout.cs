using Avalonia;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal readonly record struct NotificationGeometry(Size SurfaceSize, Size WindowSize, PixelPoint Position);

internal static class NotificationLayout
{
    internal const double ShadowPadding = 10;
    internal const double MotionDistance = 8;

    public static NotificationGeometry Calculate(PixelRect workingArea, double scaling, NotificationHudSettings settings)
    {
        var normalized = settings.Normalize();
        scaling = double.IsFinite(scaling) && scaling > 0 ? scaling : 1;
        var width = Math.Min(normalized.Width, Math.Max(1, workingArea.Width / scaling - 32));
        var height = Math.Min(normalized.Height, Math.Max(1, workingArea.Height / scaling - 36));
        var windowSize = new Size(width + 2 * ShadowPadding, height + 2 * ShadowPadding + MotionDistance);
        var surfaceWidth = (int)Math.Ceiling(width * scaling);
        var surfaceHeight = (int)Math.Ceiling(height * scaling);
        var marginX = (int)Math.Round(16 * scaling);
        var marginY = (int)Math.Round(18 * scaling);
        var x = workingArea.X + marginX + (int)Math.Round(
            Math.Max(0, workingArea.Width - surfaceWidth - 2 * marginX) * normalized.HorizontalPosition / 100);
        var y = workingArea.Y + marginY + (int)Math.Round(
            Math.Max(0, workingArea.Height - surfaceHeight - 2 * marginY) * normalized.VerticalPosition / 100);
        var shadow = (int)Math.Round(ShadowPadding * scaling);
        var maximumX = Math.Max(workingArea.X, workingArea.Right - (int)Math.Ceiling(windowSize.Width * scaling));
        var maximumY = Math.Max(workingArea.Y, workingArea.Bottom - (int)Math.Ceiling(windowSize.Height * scaling));
        return new NotificationGeometry(new Size(width, height), windowSize, new PixelPoint(
            Math.Clamp(x - shadow, workingArea.X, maximumX),
            Math.Clamp(y - shadow, workingArea.Y, maximumY)));
    }
}
