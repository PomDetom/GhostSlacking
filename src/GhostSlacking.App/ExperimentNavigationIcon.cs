using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;

namespace GhostSlacking.App;

internal static class ExperimentNavigationIcon
{
    // Degenerate unfilled edges preserve a real 20 DIP canvas: FAPathIcon normally
    // stretches Geometry.Bounds, which made the old 17 DIP flask taller/heavier
    // than the font glyphs even when their foreground brushes matched.
    public static Geometry Geometry { get; } = Geometry.Parse(
        "F0 M0,0 H0.001 M20,20 H19.999 " +
        "M7.5,1.5 H12.5 A0.425,0.425 0 0 1 12.5,2.35 H12 V7.4 L16.35,15.65 " +
        "Q17.6,18.5 14.5,18.5 H5.5 Q2.4,18.5 3.65,15.65 L8,7.4 V2.35 H7.5 A0.425,0.425 0 0 1 7.5,1.5 Z " +
        "M8.85,2.35 V7.61 L4.42,16.03 Q3.7,17.65 5.5,17.65 H14.5 Q16.3,17.65 15.58,16.03 L11.15,7.61 V2.35 Z " +
        "M6.4,12.5 H13.6 L14.05,13.35 H5.95 Z");

    public static void FollowNavigationForeground(NavigationViewItem item)
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
