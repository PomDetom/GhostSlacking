using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using FluentAvalonia.UI.Controls;

namespace GhostSlacking.App;

internal sealed class SliderNumberEditor : StackPanel
{
    internal Slider Slider { get; }
    internal NumberBox Number { get; }
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
        Spacing = 6;
        _value = value;
        Slider = new Slider { Height = 28, MinHeight = 28, Minimum = minimum, Maximum = maximum, Value = value, SmallChange = step, LargeChange = step * 4 };
        Number = new NumberBox
        {
            Minimum = minimum, Maximum = maximum, Value = value, SmallChange = step,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 106
        };
        AutomationProperties.SetName(Slider, label);
        AutomationProperties.SetName(Number, label);
        var reset = new Button { Content = "↺", Padding = new Thickness(9, 4) };
        ToolTip.SetTip(reset, resetText);
        AutomationProperties.SetName(reset, resetText + ": " + label);
        reset.Click += (_, _) => Value = defaultValue;
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
        line.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        Grid.SetColumn(Number, 1);
        line.Children.Add(Number);
        Number.Margin = new Thickness(8, 0, 0, 0);
        var units = new TextBlock { Margin = new Thickness(8, 0), Text = unit, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(units, 2);
        line.Children.Add(units);
        Grid.SetColumn(reset, 3);
        line.Children.Add(reset);
        Children.Add(line);
        Children.Add(Slider);
        Number.ValueChanged += (_, _) => Value = Number.Value;
        Slider.PropertyChanged += (_, args) =>
        {
            if (args.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty && !_synchronizing)
                Value = Math.Round(Slider.Value);
        };
    }
}
