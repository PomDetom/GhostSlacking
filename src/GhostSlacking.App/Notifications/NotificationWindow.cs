using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using FluentAvalonia.Core;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal sealed class NotificationWindow : Window
{
    internal NotificationContent Hud { get; } = new();
    private readonly TranslateTransform _translation = new();
    private readonly NotificationOpacityTransition _fade = new();
    private readonly bool _preview;
    private bool _clickable;
    public bool IsFadingOut => _fade.IsFadingOut;
    public event EventHandler? Clicked;
    public event EventHandler? ContentMetricsChanged;

    public NotificationWindow(bool preview = false)
    {
        _preview = preview;
        CanResize = false;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyBackgroundFallback = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Hud.Surface.RenderTransform = _translation;
        Hud.Surface.Margin = new Thickness(NotificationLayout.ShadowPadding, NotificationLayout.ShadowPadding,
            NotificationLayout.ShadowPadding, NotificationLayout.ShadowPadding + NotificationLayout.MotionDistance);
        Hud.Surface.PointerPressed += (_, args) =>
        {
            if (_clickable && args.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            {
                args.Handled = true;
                Clicked?.Invoke(this, EventArgs.Empty);
            }
        };
        Hud.ContentMetricsChanged += (_, _) => ContentMetricsChanged?.Invoke(this, EventArgs.Empty);
        Content = Hud;
        Configure(new NotificationHudSettings());
    }

    public void Configure(NotificationHudSettings settings, NotificationGeometry? geometry = null)
    {
        Hud.Configure(settings, geometry);
        Width = geometry?.WindowSize.Width ?? Hud.Surface.Width + 2 * NotificationLayout.ShadowPadding;
        Height = geometry?.WindowSize.Height ?? Hud.Surface.Height + 2 * NotificationLayout.ShadowPadding + NotificationLayout.MotionDistance;
    }

    public void Update(string message, UserNotificationSeverity severity, bool clickable)
    {
        _clickable = clickable;
        Hud.Update(message, severity, clickable);
    }

    internal double MeasurePreferredWidth(NotificationHudSettings settings, double scaling = 1) => Hud.MeasurePreferredWidth(settings, scaling);
    public void DisableInteraction() => _clickable = false;

    public void BeginFadeIn(DateTimeOffset now, bool reset = false)
    {
        if (_preview) return;
        _fade.Start(1, now, FAUISettings.AreAnimationsEnabled() ? AvaloniaNotificationService.FadeInDuration : TimeSpan.Zero, reset);
        ApplyFade();
    }

    public void BeginFadeOut(DateTimeOffset now, TimeSpan duration)
    {
        if (_preview || !IsVisible || _fade.IsFadingOut) return;
        _fade.Start(0, now, FAUISettings.AreAnimationsEnabled() ? duration : TimeSpan.Zero);
        ApplyFade();
        if (!_fade.IsAnimating) Hide();
    }

    public void AdvanceFade(DateTimeOffset now)
    {
        if (_preview || !IsVisible) return;
        _fade.Advance(now);
        ApplyFade();
        if (!_fade.IsAnimating && _fade.Opacity == 0) Hide();
    }

    private void ApplyFade()
    {
        Opacity = _fade.Opacity;
        _translation.Y = (1 - _fade.Opacity) * NotificationLayout.MotionDistance;
    }

}
