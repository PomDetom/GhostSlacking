using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using System.Xml.Linq;

namespace GhostSlacking.App;

internal static class SettingsNavigationIcons
{
    public static Geometry Notifications { get; } = CreateNotifications();
    public static Geometry Experiments { get; } = LoadSvg("Experiments.svg");

    private static Geometry CreateNotifications()
    {
        var outline = LoadSvg("Notifications.svg");
        // The original cap is only 2.4 DIP wide at menu size. Give its horizontal
        // center a little more room so antialiasing does not turn it into a dot.
        var body = new CombinedGeometry(GeometryCombineMode.Exclude, outline,
            new RectangleGeometry(new Rect(0, 0, 20, 3)));
        return OnCanvas(body, new RectangleGeometry(new Rect(8.3, 1.4, 3.4, 1), 0.5, 0.5));
    }

    private static Geometry LoadSvg(string fileName)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://GhostSlacking.App/Assets/Icons/{fileName}"));
        var svg = XDocument.Load(stream).Root!;
        var path = svg.Element(svg.Name.Namespace + "path")!.Attribute("d")!.Value;
        // Both bundled icons contain one filled path in a 1024-square viewBox.
        var geometry = Geometry.Parse("F1 " + path);
        geometry.Transform = new ScaleTransform(20d / 1024, 20d / 1024);
        // Normalize overlapping SVG contours before trimming their boundary, so
        // connected parts stay joined. Inset 0.22 DIP per edge to match the menu
        // font glyphs' roughly 1 DIP strokes without replacing the SVG shapes.
        var silhouette = new CombinedGeometry(GeometryCombineMode.Union, geometry, geometry);
        var boundary = silhouette.GetWidenedGeometry(new Pen
        {
            Thickness = 0.44,
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        });
        return OnCanvas(new CombinedGeometry(GeometryCombineMode.Exclude, silhouette, boundary));
    }

    private static Geometry OnCanvas(params Geometry[] shapes)
    {
        var canvas = new GeometryGroup();
        // Keep the SVG padding instead of stretching the painted bounds.
        canvas.Children.Add(Geometry.Parse("M0,0 H0.001 M20,20 H19.999"));
        foreach (var shape in shapes) canvas.Children.Add(shape);
        return canvas;
    }

    public static PathIconSource CreateSource(Geometry geometry, NavigationViewItem item)
    {
        FollowNavigationForeground(item);
        return new PathIconSource { Data = geometry, Stretch = Stretch.Uniform };
    }

    private static void FollowNavigationForeground(NavigationViewItem item)
    {
        // FluentAvalonia creates FAPathIcon, not Avalonia PathIcon. Bind the
        // generated control (not IconSource) to the navigation presenter's actual
        // state brush, just as SymbolIcon inherits it. No hard-coded gray/opacity.
        var bound = new HashSet<FAPathIcon>();
        item.LayoutUpdated += (_, _) =>
        {
            foreach (var icon in item.GetVisualDescendants().OfType<FAPathIcon>())
            {
                if (bound.Contains(icon)) continue;
                var host = icon.GetVisualAncestors().OfType<ContentPresenter>().FirstOrDefault(parent => parent.Name == "Icon");
                if (host is null) continue;
                icon.Bind(TextElement.ForegroundProperty, host.GetObservable(TextElement.ForegroundProperty));
                bound.Add(icon);
            }
        };
    }
}
