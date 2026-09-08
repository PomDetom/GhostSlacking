using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace GhostSlacking.App;

internal static class AppIcon
{
    private static readonly Lazy<WindowIcon> Icon = new(CreateIcon);

    public static WindowIcon Instance => Icon.Value;

    private static WindowIcon CreateIcon()
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(64, 64), new Vector(96, 96));
        using (var drawing = bitmap.CreateDrawingContext())
        {
            var silhouette = Geometry.Parse(
                "M 8,54 L 8,30 A 24,24 0 0 1 56,30 L 56,52 " +
                "C 52,58 47,50 42,54 C 37,59 32,50 27,54 C 22,59 17,50 8,54 Z");
            drawing.DrawGeometry(Brush.Parse("#1CB2A5"), null, silhouette);
            drawing.DrawEllipse(Brushes.White, null, new Rect(22, 25, 7, 9));
            drawing.DrawEllipse(Brushes.White, null, new Rect(38, 25, 7, 9));
        }

        return new WindowIcon(bitmap);
    }
}
