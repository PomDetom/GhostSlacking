using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using FluentAvalonia.UI.Controls;
using Path = Avalonia.Controls.Shapes.Path;

namespace GhostSlacking.App;

internal static class SettingsFieldStyles
{
    private static readonly Geometry Chevron = Geometry.Parse("M0,0 L6,6 L12,0");
    private const string ValueFieldClass = "setting-value";

    internal static void Configure(ComboBox combo)
    {
        combo.Height = combo.MinHeight = 36;
        combo.CornerRadius = new CornerRadius(6);
        combo.Padding = new Thickness(12, 0, 0, 0);
        combo.HorizontalAlignment = HorizontalAlignment.Stretch;
        combo.VerticalContentAlignment = VerticalAlignment.Center;
        combo.TemplateApplied += (_, args) =>
        {
            // Keep FluentAvalonia's selection, popup, focus and state brushes. Only
            // replace its font-dependent glyph inside the existing 32 DIP column.
            if (args.NameScope.Find<FontIcon>("DropDownGlyph") is not { Parent: Grid grid } glyph) return;
            glyph.IsVisible = false;
            var arrow = CreateArrow();
            arrow.Bind(TextElement.ForegroundProperty, glyph.GetObservable(FontIcon.ForegroundProperty));
            Grid.SetColumn(arrow, Grid.GetColumn(glyph));
            Grid.SetRow(arrow, Grid.GetRow(glyph));
            grid.Children.Add(arrow);
        };
    }

    internal static Border CreateValueField(TextBlock value, Button owner)
    {
        value.VerticalAlignment = VerticalAlignment.Center;
        value.Margin = new Thickness(12, 0, 0, 0);
        value.TextTrimming = TextTrimming.CharacterEllipsis;
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,32") };
        content.Children.Add(value);
        var arrow = CreateArrow();
        Grid.SetColumn(arrow, 1);
        content.Children.Add(arrow);
        var field = new Border
        {
            Height = 36, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Child = content,
            Margin = new Thickness(16, 0, 44, 0)
        };
        field.Classes.Add(ValueFieldClass);
        AddValueStyle(owner, null, "ComboBoxBackground", "ComboBoxBorderBrush", "ComboBoxForeground");
        AddValueStyle(owner, ":pointerover", "ComboBoxBackgroundPointerOver", "ComboBoxBorderBrushPointerOver", "ComboBoxForegroundPointerOver");
        AddValueStyle(owner, ":pressed", "ComboBoxBackgroundPressed", "ComboBoxBorderBrushPressed", "ComboBoxForegroundPressed");
        AddValueStyle(owner, ":focus-visible", "ComboBoxBackgroundFocused", "ComboBoxBackgroundBorderBrushFocused", "ComboBoxForegroundFocused");
        AddValueStyle(owner, ":disabled", "ComboBoxBackgroundDisabled", "ComboBoxBorderBrushDisabled", "ComboBoxHeaderForegroundDisabled");
        return field;
    }

    private static void AddValueStyle(Button owner, string? state, string background, string border, string foreground)
    {
        owner.Styles.Add(new Style(selector =>
        {
            var button = selector.OfType<Button>();
            if (state is not null) button = button.Class(state);
            return button.Descendant().OfType<Border>().Class(ValueFieldClass);
        })
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, new DynamicResourceExtension(background)),
                new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(border)),
                new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension(foreground))
            }
        });
        if (state is ":focus-visible" or ":disabled")
        {
            owner.Styles.Add(new Style(selector => selector.OfType<Button>().Class(state)
                .Descendant().OfType<Border>().Name("SettingsDropDownArrow"))
            {
                Setters = { new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension(
                    state == ":disabled" ? "ComboBoxDropDownGlyphForegroundDisabled" : "ComboBoxDropDownGlyphForegroundFocused")) }
            });
        }
    }

    private static Border CreateArrow()
    {
        var path = new Path
        {
            Data = Chevron, Width = 12, Height = 6, Stretch = Stretch.Fill,
            StrokeThickness = 1.5, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var host = new Border
        {
            Name = "SettingsDropDownArrow", Width = 32, IsHitTestVisible = false,
            UseLayoutRounding = false, Child = path
        };
        host.Styles.Add(new Style(selector => selector.OfType<Border>())
        {
            Setters = { new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension("ComboBoxDropDownGlyphForeground")) }
        });
        path.Bind(Path.StrokeProperty, host.GetObservable(TextElement.ForegroundProperty));
        return host;
    }
}
