using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

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
        slider.Height = 32;
        slider.MinHeight = 32;
        slider.Template = new FuncControlTemplate<Slider>((control, scope) =>
        {
            var palette = AdjustmentPalette.For(control.ActualThemeVariant);
            var background = new Border { Height = 4, Margin = new Thickness(12, 0), CornerRadius = new CornerRadius(2), Background = AdjustmentPalette.Brush(palette.Border), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            var progress = new Border { Height = 4, Margin = new Thickness(12, 0), CornerRadius = new CornerRadius(2), Background = AdjustmentPalette.Brush(palette.Accent), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            var dot = new Border { Width = 12, Height = 12, Background = AdjustmentPalette.Brush(palette.Accent), CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var thumb = new Thumb
            {
                Width = 24, Height = 32, MinWidth = 24, MinHeight = 32,
                Template = new FuncControlTemplate<Thumb>((_, _) => new Grid { Background = Brushes.Transparent, Children = { dot } })
            };
            Button TrackButton(string name)
            {
                var button = new Button
                {
                    Name = name, Focusable = false, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                    Template = new FuncControlTemplate<Button>((_, _) => new Border { Background = Brushes.Transparent })
                };
                scope.Register(name, button);
                return button;
            }
            var decrease = TrackButton("PART_DecreaseButton");
            var increase = TrackButton("PART_IncreaseButton");
            var track = new Track { Name = "PART_Track", Orientation = Orientation.Horizontal, Thumb = thumb, DecreaseButton = decrease, IncreaseButton = increase };
            scope.Register("PART_Track", track);
            track.Bind(Track.MinimumProperty, new Binding(nameof(control.Minimum)) { Source = control });
            track.Bind(Track.MaximumProperty, new Binding(nameof(control.Maximum)) { Source = control });
            track.Bind(Track.ValueProperty, new Binding(nameof(control.Value)) { Source = control, Mode = BindingMode.TwoWay });
            track.Bind(Track.IsDirectionReversedProperty, new Binding(nameof(control.IsDirectionReversed)) { Source = control });
            // Preserve the original RepeatButton step and theme feedback, confined to short ends.
            RepeatButton StepEnd(string name, HorizontalAlignment alignment) => new()
            {
                Name = name, Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Focusable = false,
                HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Center
            };
            var decreaseStep = StepEnd("StepDecreaseEnd", HorizontalAlignment.Left);
            var increaseStep = StepEnd("StepIncreaseEnd", HorizontalAlignment.Right);
            decreaseStep.Click += (_, _) => control.Value -= control.LargeChange;
            increaseStep.Click += (_, _) => control.Value += control.LargeChange;
            var root = new Grid { Children = { background, progress, track, decreaseStep, increaseStep } };
            void Refresh()
            {
                var p = AdjustmentPalette.For(control.ActualThemeVariant);
                background.Background = AdjustmentPalette.Brush(p.Border);
                progress.Background = AdjustmentPalette.Brush(p.Accent);
                dot.Background = AdjustmentPalette.Brush(p.Accent);
                var available = Math.Max(0, root.Bounds.Width - thumb.Width);
                var fraction = Math.Clamp((control.Value - control.Minimum) / Math.Max(1, control.Maximum - control.Minimum), 0, 1);
                progress.Width = available * fraction;
                // Leave the visible dot available for dragging, even when it overlaps an end.
                var dotLeft = thumb.Width / 2 + available * fraction - dot.Width / 2;
                foreach (var (button, left) in new[] { (decreaseStep, 0d), (increaseStep, Math.Max(0, root.Bounds.Width - increaseStep.Width)) })
                {
                    var hole = new Rect(dotLeft - left, 0, dot.Width, root.Bounds.Height);
                    var area = new Rect(0, 0, button.Width, root.Bounds.Height);
                    button.Clip = area.Intersects(hole)
                        ? new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(area), new RectangleGeometry(hole))
                        : null;
                }
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
