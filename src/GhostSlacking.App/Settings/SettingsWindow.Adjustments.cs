using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using GhostSlacking.Core;
using System.Globalization;
using Button = Avalonia.Controls.Button;
using ComboBox = Avalonia.Controls.ComboBox;
using Control = Avalonia.Controls.Control;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using KeyEventArgs = Avalonia.Input.KeyEventArgs;
using Orientation = Avalonia.Layout.Orientation;
using ToolTip = Avalonia.Controls.ToolTip;

namespace GhostSlacking.App;

internal sealed record SettingsAdjustmentRow(TextBlock Value, Button Row, Button Reset, Border Field);

internal sealed partial class SettingsWindow : AppWindow
{

    private NotificationHudSettings GetNotificationHudSettings() => new NotificationHudSettings
    {
        CornerRadius = _notificationRadius.Value,
        Width = _notificationWidth.Value,
        Height = _notificationHeight.Value,
        HorizontalPosition = _notificationHorizontalPosition.Value,
        VerticalPosition = _notificationVerticalPosition.Value
    }.Normalize();

    private Control CreateAdjustmentRow(UiLanguage language, AdjustmentKind kind, string descriptionKey)
    {
        var spec = SettingAdjustment.For(kind);
        var title = LocalizedText(language, spec.Label);
        title.FontWeight = FontWeight.Medium;
        title.Foreground = _primaryBrush;
        var description = LocalizedText(language, descriptionKey);
        description.FontSize = 12;
        description.Foreground = _secondaryBrush;
        description.TextWrapping = TextWrapping.Wrap;
        var copy = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { title, description } };
        var value = new TextBlock();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(SettingEditorColumnWidth(language))));
        _editorColumns.Add((grid.ColumnDefinitions[1], false));
        grid.Children.Add(copy);
        var button = new Button
        {
            Content = grid, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 14)
        };
        var field = SettingsFieldStyles.CreateValueField(value, button);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
        var reset = CreateIconButton(Symbol.Refresh, "resetSetting");
        reset.HorizontalAlignment = HorizontalAlignment.Right;
        reset.VerticalAlignment = VerticalAlignment.Center;
        reset.Margin = new Thickness(0, 0, 16, 0);
        reset.Click += (_, args) =>
        {
            args.Handled = true;
            CloseAdjustment();
            CancelKeyCapture();
            ApplyAdjustmentResult(DefaultSettings, kind);
        };
        // Siblings keep the entire card clickable without nesting interactive controls.
        var actions = new Grid { Children = { button, reset } };
        _adjustmentValues[kind] = new(value, button, reset, field);
        button.Click += (_, _) => OpenAdjustment(kind, button, actions);
        return new Border
        {
            Background = _cardBrush, BorderBrush = _fieldBorderBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Child = actions
        };
    }

    internal void OpenAdjustment(AdjustmentKind kind, Control anchor, Control? inputPassThrough = null)
    {
        CancelKeyCapture();
        var same = _activeAdjustment?.Kind == kind;
        CloseAdjustment();
        if (same) return;
        var flyout = new SettingsAdjustmentFlyout(GetSettings(), kind, result => ApplyAdjustmentResult(result, kind));
        _activeAdjustment = flyout;
        flyout.Closed += (_, _) => { if (ReferenceEquals(_activeAdjustment, flyout)) _activeAdjustment = null; };
        flyout.OpenAt(anchor, inputPassThrough);
    }

    private void CloseAdjustment()
    {
        _activeAdjustment?.Hide();
        _activeAdjustment?.Dispose();
        _activeAdjustment = null;
    }

    internal void ApplyAdjustmentResult(AppSettings result, AdjustmentKind kind)
    {
        var normalized = result.Normalize();
        _initializing = true;
        try
        {
            switch (kind)
            {
                case AdjustmentKind.Diameter: _diameter.Value = normalized.RevealDiameterPx; break;
                case AdjustmentKind.DiameterStep: _diameterStep.Value = normalized.RevealDiameterStepPx; break;
                case AdjustmentKind.SoftEdgeWidth: _softEdgeWidth.Value = normalized.RevealSoftEdgeWidthPx; break;
                case AdjustmentKind.BlurLevel: SelectChoice(_blurLevel, normalized.RevealBlurLevel); break;
                case AdjustmentKind.Shape: SelectChoice(_shape, normalized.RevealShape); break;
                case AdjustmentKind.FrameRate: SelectChoice(_peekFrameRateLimit, normalized.PeekFrameRateLimit); break;
                case AdjustmentKind.NotificationRadius: _notificationRadius.Value = normalized.NotificationHud.CornerRadius; break;
                case AdjustmentKind.NotificationWidth: _notificationWidth.Value = normalized.NotificationHud.Width; break;
                case AdjustmentKind.NotificationHeight: _notificationHeight.Value = normalized.NotificationHud.Height; break;
                case AdjustmentKind.NotificationHorizontal: _notificationHorizontalPosition.Value = normalized.NotificationHud.HorizontalPosition; break;
                case AdjustmentKind.NotificationVertical: _notificationVerticalPosition.Value = normalized.NotificationHud.VerticalPosition; break;
            }
        }
        finally { _initializing = false; }
        OnSettingsEdited();
    }

    private void RefreshAdjustmentValues()
    {
        var settings = GetSettings();
        foreach (var (kind, row) in _adjustmentValues)
        {
            var formatted = SettingAdjustment.Format(settings, kind, CurrentLanguage);
            row.Value.Text = formatted;
            var label = UiText.Text(CurrentLanguage, SettingAdjustment.For(kind).Label);
            Avalonia.Automation.AutomationProperties.SetName(row.Row,
                label + ": " + formatted);
            var resetLabel = UiText.Text(CurrentLanguage, "resetSetting") + ": " + label;
            ToolTip.SetTip(row.Reset, resetLabel);
            Avalonia.Automation.AutomationProperties.SetName(row.Reset, resetLabel);
        }
    }
}
