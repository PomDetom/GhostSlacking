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
        var copy = new StackPanel { Spacing = 4, Children = { title, description } };
        var value = new TextBlock { Foreground = _primaryBrush, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 240 };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(copy);
        value.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        var button = new Button
        {
            Content = grid, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch, Background = _cardBrush,
            BorderBrush = _fieldBorderBrush, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 14)
        };
        _adjustmentValues[kind] = (value, button);
        button.Click += (_, _) => OpenAdjustment(kind, button);
        return button;
    }

    internal void OpenAdjustment(AdjustmentKind kind, Control anchor)
    {
        CancelKeyCapture();
        var same = _activeAdjustment?.Kind == kind;
        CloseAdjustment();
        if (same) return;
        var flyout = new SettingsAdjustmentFlyout(GetSettings(), kind, result => ApplyAdjustmentResult(result, kind));
        _activeAdjustment = flyout;
        flyout.Closed += (_, _) => { if (ReferenceEquals(_activeAdjustment, flyout)) _activeAdjustment = null; };
        flyout.OpenAt(anchor);
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
            row.Value.Text = formatted + "  ⌄";
            Avalonia.Automation.AutomationProperties.SetName(row.Row,
                UiText.Text(CurrentLanguage, SettingAdjustment.For(kind).Label) + ": " + formatted);
        }
    }
}
