using Avalonia.Controls;
using FluentAvalonia.UI.Windowing;
using GhostSlacking.Core;
using Button = Avalonia.Controls.Button;
using Control = Avalonia.Controls.Control;
using ToolTip = Avalonia.Controls.ToolTip;

namespace GhostSlacking.App;

internal sealed record SettingsAdjustmentRow(TextBlock Value, Button Field, Button Reset);

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
        var value = new TextBlock();
        var field = SettingsFieldStyles.CreateValueField(value);
        var editor = CreateResettable(field, () =>
        {
            CloseAdjustment();
            CancelKeyCapture();
            ApplyAdjustmentResult(DefaultSettings, kind);
        }, out var reset);
        var row = CreateSettingRow(language, SettingAdjustment.For(kind).Label, descriptionKey, editor);
        _adjustmentValues[kind] = new(value, field, reset);
        field.Click += (_, _) => OpenAdjustment(kind, row, editor);
        return row;
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
            Avalonia.Automation.AutomationProperties.SetName(row.Field,
                label + ": " + formatted);
            var resetLabel = UiText.Text(CurrentLanguage, "resetSetting") + ": " + label;
            ToolTip.SetTip(row.Reset, resetLabel);
            Avalonia.Automation.AutomationProperties.SetName(row.Reset, resetLabel);
        }
    }
}
