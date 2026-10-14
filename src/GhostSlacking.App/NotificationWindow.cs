using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.Core;
using GhostSlacking.Core;
using Path = Avalonia.Controls.Shapes.Path;

namespace GhostSlacking.App;

internal sealed class NotificationWindow : Window
{
    private static readonly Geometry InfoIcon = Geometry.Parse("M 22,12 A 10,10 0 1 1 12,2 A 10,10 0 0 1 22,12 M 12,11 L 12,17 M 12,7 L 12,7.1");
    private static readonly Geometry SuccessIcon = Geometry.Parse("M 22,12 A 10,10 0 1 1 12,2 A 10,10 0 0 1 22,12 M 7,12 L 10,15 L 17,8");
    private static readonly Geometry ErrorIcon = Geometry.Parse("M 22,12 A 10,10 0 1 1 12,2 A 10,10 0 0 1 22,12 M 12,7 L 12,12 M 12,16 L 12,16.1");
    private static readonly Geometry UpdateIcon = Geometry.Parse("M 22,12 A 10,10 0 1 1 12,2 A 10,10 0 0 1 22,12 M 8,12 L 12,8 L 16,12 M 12,8 L 12,17");
    private readonly Border _surface;
    private readonly TextBlock _message;
    private readonly Path _icon;
    private readonly TranslateTransform _translation = new();
    private readonly NotificationOpacityTransition _fade = new();
    private readonly bool _preview;
    private UserNotificationSeverity _severity;
    private bool _clickable;

    public bool IsFadingOut => _fade.IsFadingOut;
    public event EventHandler? Clicked;

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
        _icon = new Path
        {
            Data = InfoIcon,
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center
        };
        _message = new TextBlock
        {
            Width = 65,
            FontSize = 13,
            FontWeight = FontWeight.Medium,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetLiveSetting(_message, AutomationLiveSetting.Polite);
        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("18,9,65"),
            Width = 92,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(_icon);
        Grid.SetColumn(_message, 2);
        content.Children.Add(_message);
        _surface = new Border
        {
            BorderThickness = new Thickness(1),
            Child = content,
            RenderTransform = _translation,
            Margin = new Thickness(NotificationLayout.ShadowPadding, NotificationLayout.ShadowPadding,
                NotificationLayout.ShadowPadding, NotificationLayout.ShadowPadding + NotificationLayout.MotionDistance)
        };
        _surface.PointerPressed += (_, args) =>
        {
            if (_clickable && args.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            {
                args.Handled = true;
                Clicked?.Invoke(this, EventArgs.Empty);
            }
        };
        Content = _surface;
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        Configure(new NotificationHudSettings());
        ApplyPalette();
    }

    public void Configure(NotificationHudSettings settings, NotificationGeometry? geometry = null)
    {
        var normalized = settings.Normalize();
        var size = geometry?.SurfaceSize ?? new Size(normalized.Width, normalized.Height);
        _surface.Width = size.Width;
        _surface.Height = size.Height;
        _surface.CornerRadius = new CornerRadius(Math.Min(normalized.CornerRadius, Math.Min(size.Width, size.Height) / 2));
        Width = geometry?.WindowSize.Width ?? size.Width + 2 * NotificationLayout.ShadowPadding;
        Height = geometry?.WindowSize.Height ?? size.Height + 2 * NotificationLayout.ShadowPadding + NotificationLayout.MotionDistance;
    }

    public void Update(string message, UserNotificationSeverity severity, bool clickable)
    {
        _message.Text = string.Join(" ", message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        _severity = severity;
        _clickable = clickable;
        _icon.Data = clickable ? UpdateIcon : severity switch
        {
            UserNotificationSeverity.Success => SuccessIcon,
            UserNotificationSeverity.Error => ErrorIcon,
            _ => InfoIcon
        };
        _surface.Cursor = clickable ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        ApplyPalette();
    }

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

    private void ApplyPalette()
    {
        var palette = AvaloniaNotificationService.ResolvePalette(ActualThemeVariant, _severity);
        _surface.Background = new SolidColorBrush(palette.Surface);
        _surface.BorderBrush = new SolidColorBrush(palette.Border);
        _message.Foreground = new SolidColorBrush(palette.Message);
        _icon.Stroke = new SolidColorBrush(palette.Icon);
        _surface.BoxShadow = BoxShadows.Parse(AppTheme.IsDark(ActualThemeVariant)
            ? "0 4 12 0 #66000000" : "0 4 12 0 #2017332D");
    }
}
