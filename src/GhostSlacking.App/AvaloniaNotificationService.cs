using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.Core;
using GhostSlacking.Core;
using DrawingPoint = System.Drawing.Point;

namespace GhostSlacking.App;

internal sealed class AvaloniaNotificationService : IUserNotificationService
{
    internal static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(2.5);
    internal static readonly TimeSpan ClickableDisplayDuration = TimeSpan.FromSeconds(4.5);
    internal static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(150);
    internal static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(200);
    private readonly Func<DrawingPoint> _getCursorPosition;
    private readonly Func<nint, NativeResult>? _bringToFront;
    private readonly Func<nint, bool, NativeResult>? _setClickThrough;
    private readonly ILogger _logger;
    private readonly DispatcherTimer _timer;
    private readonly TransientNotificationState _state = new(DisplayDuration);
    private AppSettings _settings;
    private NotificationWindow? _window;
    private PixelPoint _screenPoint;
    private DateTimeOffset _nextLayoutCheck;
    private bool _disposed;

    internal static UserNotificationPalette ResolvePalette(ThemeVariant? variant, UserNotificationSeverity severity)
    {
        var palette = AppTheme.Palette(variant);
        return new UserNotificationPalette(palette.Surface, palette.Border, palette.Primary, severity switch
        {
            UserNotificationSeverity.Error => palette.Error,
            UserNotificationSeverity.Success => palette.Success,
            _ => palette.Accent
        });
    }

    public AvaloniaNotificationService(
        Func<DrawingPoint> getCursorPosition,
        ILogger logger,
        Func<nint, NativeResult>? bringToFront = null,
        AppSettings? settings = null,
        Func<nint, bool, NativeResult>? setClickThrough = null)
    {
        _getCursorPosition = getCursorPosition;
        _bringToFront = bringToFront;
        _setClickThrough = setClickThrough;
        _logger = logger;
        _settings = (settings ?? new AppSettings()).Normalize();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTimerTick;
    }

    public void Configure(AppSettings settings)
    {
        if (_disposed) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Configure(settings));
            return;
        }

        _settings = settings.Normalize();
        RefreshLayout();
    }

    public void RefreshLayout()
    {
        if (_disposed) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshLayout);
            return;
        }

        if (_window is null) return;
        var screen = _window.Screens.ScreenFromPoint(_screenPoint) ?? _window.Screens.Primary;
        if (screen is null) return;
        var geometry = NotificationLayout.Calculate(screen.WorkingArea, screen.Scaling, _settings.NotificationHud);
        _window.Configure(_settings.NotificationHud, geometry);
        if (_window.Position != geometry.Position) _window.Position = geometry.Position;
    }

    public void Show(string message, UserNotificationSeverity severity, Action? clickAction = null,
        UserNotificationSource source = UserNotificationSource.General)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message)) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(message, severity, clickAction, source));
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_state.Current is { } current && UserNotificationPolicy.ShouldSuppress(source, current, _state.IsPaused, now))
            {
                _logger.Log(LogLevel.Debug, $"NotificationSuppressed source={source} current={current.Source}");
                return;
            }

            var duration = clickAction is null ? DisplayDuration : ClickableDisplayDuration;
            _state.Show(message, severity, now, clickAction, duration, source);
            if (_window is null)
            {
                _window = new NotificationWindow();
                _window.Clicked += OnWindowClicked;
                _window.PointerEntered += (_, _) => PauseForHover();
                _window.PointerExited += (_, _) => ResumeAfterHover();
            }

            var cursor = _getCursorPosition();
            _screenPoint = new PixelPoint(cursor.X, cursor.Y);
            var wasVisible = _window.IsVisible;
            var wasFadingOut = _window.IsFadingOut;
            _window.Update(message, severity, clickAction is not null);
            RefreshLayout();
            if (!wasVisible || wasFadingOut) _window.BeginFadeIn(now, reset: !wasVisible);
            if (!wasVisible) _window.Show();
            // Re-evaluate after the HWND has moved to the selected monitor's DPI.
            RefreshLayout();
            SetClickThrough(clickAction is null);
            if (_state.Current is null) return;
            if (_bringToFront is not null)
            {
                var result = _bringToFront(_window.TryGetPlatformHandle()?.Handle ?? 0);
                if (!result.Success) _logger.Log(LogLevel.Warning, $"NotificationRaiseFailed error={result.ErrorCode}");
            }

            _timer.Start();
            if (clickAction is not null && _window.IsPointerOver) PauseForHover();
        }
        catch (Exception exception)
        {
            _state.Clear();
            _window?.Hide();
            _timer.Stop();
            _logger.Log(LogLevel.Warning, "Avalonia notification could not be shown.", exception);
        }
    }

    private void SetClickThrough(bool enabled)
    {
        if (_window is null || _setClickThrough is null) return;
        var result = _setClickThrough(_window.TryGetPlatformHandle()?.Handle ?? 0, enabled);
        if (!result.Success)
        {
            // Do not leave an interactive window without the native no-activation safeguard.
            _state.Clear();
            _window.Hide();
            _timer.Stop();
            _logger.Log(LogLevel.Warning, $"NotificationClickThroughFailed error={result.ErrorCode}");
        }
    }

    private void OnWindowClicked(object? sender, EventArgs args)
    {
        if (_state.Expire(DateTimeOffset.UtcNow))
        {
            _window?.Hide();
            return;
        }

        var action = _state.TakeClickAction();
        if (action is null) return;
        _window?.DisableInteraction();
        SetClickThrough(true);
        _window?.BeginFadeOut(DateTimeOffset.UtcNow, FadeOutDuration);
        _timer.Start();
        try { action(); }
        catch (Exception exception) { _logger.Log(LogLevel.Warning, "Notification click action failed.", exception); }
    }

    internal void PauseForHover()
    {
        var now = DateTimeOffset.UtcNow;
        if (_state.Expire(now))
        {
            _window?.Hide();
            _timer.Stop();
            return;
        }
        if (_state.Pause(now)) _window?.BeginFadeIn(now);
    }

    internal void ResumeAfterHover() => _state.Resume(DateTimeOffset.UtcNow);

    private void OnTimerTick(object? sender, EventArgs args)
    {
        var now = DateTimeOffset.UtcNow;
        if (_window?.IsVisible == true && now >= _nextLayoutCheck)
        {
            RefreshLayout();
            _nextLayoutCheck = now.AddMilliseconds(100);
        }
        var fadeDuration = FAUISettings.AreAnimationsEnabled() ? FadeOutDuration : TimeSpan.Zero;
        if (_state.Current is { } current && !_state.IsPaused && now >= current.ExpiresAt - fadeDuration)
            _window?.BeginFadeOut(now, current.ExpiresAt - now);
        _window?.AdvanceFade(now);
        if (_state.Expire(now)) _window?.Hide();
        if (_state.Current is null && _window?.IsVisible != true) _timer.Stop();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _state.Clear();
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _window?.Close();
        _window = null;
        GC.SuppressFinalize(this);
    }
}
