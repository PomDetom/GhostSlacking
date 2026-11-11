using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace GhostSlacking.App;

internal readonly record struct AdjustmentPalette(Color Surface, Color Field, Color Border, Color Text, Color Muted, Color Accent, Color Soft)
{
    internal static AdjustmentPalette For(ThemeVariant? theme)
    {
        var dark = AppTheme.IsDark(theme);
        return new(
            Color.Parse(dark ? "#272B2D" : "#FFFFFF"), Color.Parse(dark ? "#202526" : "#FAFCFC"),
            Color.Parse(dark ? "#3D4446" : "#E4E9E9"), Color.Parse(dark ? "#EFF3F3" : "#202829"),
            Color.Parse(dark ? "#A3B0B2" : "#728083"), Color.Parse(dark ? "#58D5C7" : "#087F79"),
            Color.Parse(dark ? "#173E3B" : "#E7F6F3"));
    }
    internal static SolidColorBrush Brush(Color color) => new(color);
}

internal static class AdjustmentStyles
{
    internal static void Button(Button button, AdjustmentPalette palette, bool primary = false, bool selected = false)
    {
        button.CornerRadius = new CornerRadius(8);
        button.Padding = new Thickness(12, 7);
        button.BorderThickness = new Thickness(1);
        button.Background = AdjustmentPalette.Brush(primary ? palette.Accent : selected ? palette.Soft : palette.Surface);
        button.BorderBrush = AdjustmentPalette.Brush(primary || selected ? palette.Accent : palette.Border);
        button.Foreground = AdjustmentPalette.Brush(primary ? (palette.Surface.R < 100 ? Color.Parse("#102F2C") : Colors.White) : selected ? palette.Accent : palette.Text);
    }

    internal static void Link(Button button, AdjustmentPalette palette)
    {
        button.Background = Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
        button.Foreground = AdjustmentPalette.Brush(palette.Accent);
        button.Padding = new Thickness(0, 3);
        button.FontSize = 12;
    }

    internal static void Slider(Slider slider)
    {
        slider.Height = 26;
        slider.MinHeight = 26;
        slider.Template = new FuncControlTemplate<Slider>((control, scope) =>
        {
            var palette = AdjustmentPalette.For(control.ActualThemeVariant);
            var background = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = AdjustmentPalette.Brush(palette.Border), VerticalAlignment = VerticalAlignment.Center };
            var progress = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = AdjustmentPalette.Brush(palette.Accent), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var thumb = new Thumb
            {
                Width = 12, Height = 12, MinWidth = 12, MinHeight = 12,
                Template = new FuncControlTemplate<Thumb>((_, _) => new Border { Background = AdjustmentPalette.Brush(palette.Accent), CornerRadius = new CornerRadius(6) })
            };
            var decrease = new RepeatButton { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Focusable = false };
            var increase = new RepeatButton { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Focusable = false };
            decrease.Click += (_, _) => control.Value -= control.LargeChange;
            increase.Click += (_, _) => control.Value += control.LargeChange;
            var track = new Track { Name = "PART_Track", Orientation = Orientation.Horizontal, Thumb = thumb, DecreaseButton = decrease, IncreaseButton = increase };
            scope.Register("PART_Track", track);
            track.Bind(Track.MinimumProperty, new Binding(nameof(control.Minimum)) { Source = control });
            track.Bind(Track.MaximumProperty, new Binding(nameof(control.Maximum)) { Source = control });
            track.Bind(Track.ValueProperty, new Binding(nameof(control.Value)) { Source = control, Mode = BindingMode.TwoWay });
            var root = new Grid { Children = { background, progress, track } };
            void Refresh()
            {
                var p = AdjustmentPalette.For(control.ActualThemeVariant);
                background.Background = AdjustmentPalette.Brush(p.Border);
                progress.Background = AdjustmentPalette.Brush(p.Accent);
                if (thumb.GetVisualChildren().FirstOrDefault() is Border dot) dot.Background = AdjustmentPalette.Brush(p.Accent);
                var available = Math.Max(0, root.Bounds.Width - thumb.Width);
                progress.Width = thumb.Width / 2 + available * Math.Clamp((control.Value - control.Minimum) / Math.Max(1, control.Maximum - control.Minimum), 0, 1);
            }
            root.SizeChanged += (_, _) => Refresh();
            control.PropertyChanged += (_, args) =>
            {
                if (args.Property == RangeBase.ValueProperty || args.Property == RangeBase.MinimumProperty || args.Property == RangeBase.MaximumProperty) Refresh();
            };
            control.ActualThemeVariantChanged += (_, _) => Refresh();
            return root;
        });
    }
}
