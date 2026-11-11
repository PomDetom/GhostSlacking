using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;

namespace GhostSlacking.App;

internal sealed class SliderNumberEditor : StackPanel
{
    internal Slider Slider { get; }
    internal NumberBox Number { get; }
    internal Button ResetButton { get; }
    private readonly TextBlock _minimumText;
    private readonly TextBlock _maximumText;
    private bool _synchronizing;
    private double _value;
    public event EventHandler? ValueChanged;

    public double Value
    {
        get => _value;
        set
        {
            var next = double.IsFinite(value) ? Math.Clamp(value, Slider.Minimum, Slider.Maximum) : _value;
            if (_synchronizing) return;
            var changed = next != _value;
            _synchronizing = true;
            _value = next;
            Number.Value = next;
            Slider.Value = next;
            _synchronizing = false;
            if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public SliderNumberEditor(string label, string unit, double value, double minimum, double maximum,
        double step, double defaultValue, string resetText)
    {
        Spacing = 4;
        _value = value;
        Slider = new Slider { Height = 28, MinHeight = 28, Minimum = minimum, Maximum = maximum, Value = value, SmallChange = step, LargeChange = step * 4 };
        Number = new NumberBox
        {
            Minimum = minimum, Maximum = maximum, Value = value, SmallChange = step,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden, Width = 86,
            CornerRadius = new CornerRadius(8), MinHeight = 34
        };
        AutomationProperties.SetName(Slider, label);
        AutomationProperties.SetName(Number, label);
        Number.TemplateApplied += (_, _) => ApplyPalette();
        ResetButton = new Button { Content = resetText, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        AutomationProperties.SetName(ResetButton, resetText + ": " + label);
        ResetButton.Click += (_, _) => Value = defaultValue;
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 2, 0, 2) };
        line.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        Grid.SetColumn(Number, 1);
        line.Children.Add(Number);
        Number.Margin = new Thickness(8, 0, 0, 0);
        var units = new TextBlock { Margin = new Thickness(8, 0), Text = unit, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(units, 2);
        line.Children.Add(units);
        Children.Add(line);
        AdjustmentStyles.Slider(Slider);
        Children.Add(Slider);
        var limits = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        _minimumText = new TextBlock { Text = $"{minimum:0.##} {unit}", FontSize = 11 };
        _maximumText = new TextBlock { Text = $"{maximum:0.##} {unit}", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
        limits.Children.Add(_minimumText);
        Grid.SetColumn(_maximumText, 1);
        limits.Children.Add(_maximumText);
        Children.Add(limits);
        Children.Add(ResetButton);
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        ApplyPalette();
        Number.ValueChanged += (_, _) => Value = Number.Value;
        Slider.PropertyChanged += (_, args) =>
        {
            if (args.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty && !_synchronizing)
                Value = Math.Round(Slider.Value);
        };
    }

    private void ApplyPalette()
    {
        var palette = AdjustmentPalette.For(ActualThemeVariant);
        Number.Background = AdjustmentPalette.Brush(palette.Field);
        Number.BorderBrush = AdjustmentPalette.Brush(palette.Border);
        if (Number.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } input)
        {
            input.Background = AdjustmentPalette.Brush(palette.Field);
            input.BorderBrush = AdjustmentPalette.Brush(palette.Border);
            input.CornerRadius = new CornerRadius(8);
        }
        _minimumText.Foreground = _maximumText.Foreground = AdjustmentPalette.Brush(palette.Muted);
        AdjustmentStyles.Link(ResetButton, palette);
    }
}
