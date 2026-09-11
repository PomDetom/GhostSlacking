using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using GhostSlacking.Core;
using DrawingPoint = System.Drawing.Point;

namespace GhostSlacking.App;

internal enum UserNotificationSeverity
{
    Info,
    Error
}

internal interface IUserNotificationService : IDisposable
{
    void Show(string message, UserNotificationSeverity severity, Action? clickAction = null);
}

internal readonly record struct TransientNotification(
    string Message,
    UserNotificationSeverity Severity,
    DateTimeOffset ExpiresAt,
    Action? ClickAction);

internal readonly record struct UserNotificationPalette(
    Color Surface,
    Color Border,
    Color Title,
    Color Message,
    Color Badge,
    Color Accent);

internal sealed class TransientNotificationState(TimeSpan duration)
{
    private TimeSpan? _pausedRemaining;

    public TransientNotification? Current { get; private set; }
    public bool IsPaused => _pausedRemaining is not null;

    public void Show(
        string message,
        UserNotificationSeverity severity,
        DateTimeOffset now,
        Action? clickAction = null,
        TimeSpan? displayDuration = null)
    {
        var selectedDuration = displayDuration ?? duration;
        if (selectedDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(displayDuration));
        }

        _pausedRemaining = null;
        Current = new TransientNotification(message, severity, now.Add(selectedDuration), clickAction);
    }

    public bool Expire(DateTimeOffset now)
    {
        if (Current is null || IsPaused || now < Current.Value.ExpiresAt)
        {
            return false;
        }

        _pausedRemaining = null;
        Current = null;
        return true;
    }

    public bool Pause(DateTimeOffset now)
    {
        if (Current is null || Current.Value.ClickAction is null || IsPaused)
        {
            return false;
        }

        var remaining = Current.Value.ExpiresAt - now;
        if (remaining <= TimeSpan.Zero)
        {
            Current = null;
            return false;
        }

        _pausedRemaining = remaining;
        return true;
    }

    public TimeSpan? Resume(DateTimeOffset now)
    {
        if (Current is null || _pausedRemaining is not { } remaining)
        {
            return null;
        }

        Current = Current.Value with { ExpiresAt = now.Add(remaining) };
        _pausedRemaining = null;
        return remaining;
    }

    public Action? TakeClickAction()
    {
        var action = Current?.ClickAction;
        if (action is null)
        {
            return null;
        }

        _pausedRemaining = null;
        Current = null;
        return action;
    }
}

internal sealed class AvaloniaNotificationService : IUserNotificationService
{
    internal static readonly TimeSpan DisplayDuration = TimeSpan.FromMilliseconds(2500);
    internal static readonly TimeSpan ClickableDisplayDuration = TimeSpan.FromMilliseconds(4500);
    private const double WindowWidth = 360;
    private const double WindowHeight = 116;
    private const int ScreenMarginPx = 16;

    private readonly Func<DrawingPoint> _getCursorPosition;
    private readonly ILogger _logger;
    private readonly DispatcherTimer _timer;
    private readonly TransientNotificationState _state = new(DisplayDuration);
    private NotificationWindow? _window;
    private bool _disposed;

    internal static UserNotificationPalette ResolvePalette(
        ThemeVariant? themeVariant,
        UserNotificationSeverity severity)
    {
        var dark = AppTheme.IsDark(themeVariant);
        var error = severity == UserNotificationSeverity.Error;
        return new UserNotificationPalette(
            dark ? AppTheme.DarkFieldColor : Color.Parse("#FFFFFF"),
            error ? Color.Parse("#EF4444") : AppTheme.AccentColor,
            Color.Parse(dark ? "#F5F5F5" : "#172033"),
            Color.Parse(dark ? "#A3A3A3" : "#667085"),
            Color.Parse(error
                ? dark ? "#3F1010" : "#FEE2E2"
                : dark ? "#103B37" : "#DDF7F5"),
            error ? Color.Parse("#EF4444") : AppTheme.AccentColor);
    }

    public AvaloniaNotificationService(Func<DrawingPoint> getCursorPosition, ILogger logger)
    {
        _getCursorPosition = getCursorPosition;
        _logger = logger;
        _timer = new DispatcherTimer { Interval = DisplayDuration };
        _timer.Tick += OnTimerTick;
    }

    public void Show(string message, UserNotificationSeverity severity, Action? clickAction = null)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(message, severity, clickAction));
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var displayDuration = clickAction is null ? DisplayDuration : ClickableDisplayDuration;
            _state.Show(message, severity, now, clickAction, displayDuration);
            if (_window is null)
            {
                _window = new NotificationWindow();
                _window.Clicked += OnWindowClicked;
                _window.PointerEntered += OnWindowPointerEntered;
                _window.PointerExited += OnWindowPointerExited;
            }

            _window.Update(message, severity, clickAction is not null);
            PositionWindow(_window, _getCursorPosition());
            if (!_window.IsVisible)
            {
                _window.Show();
            }

            _timer.Stop();
            _timer.Interval = displayDuration;
            _timer.Start();
            if (clickAction is not null && _window.IsPointerOver && _state.Pause(now))
            {
                _timer.Stop();
            }
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "Avalonia notification could not be shown.", exception);
        }
    }

    private void OnWindowClicked(object? sender, EventArgs args)
    {
        try
        {
            var action = _state.TakeClickAction();
            if (action is null)
            {
                return;
            }

            _timer.Stop();
            _window?.Hide();
            action();
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "Notification click action failed.", exception);
        }
    }

    private static void PositionWindow(Window window, DrawingPoint cursor)
    {
        var screen = window.Screens.ScreenFromPoint(new PixelPoint(cursor.X, cursor.Y)) ?? window.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var width = (int)Math.Ceiling(WindowWidth * screen.Scaling);
        var height = (int)Math.Ceiling(WindowHeight * screen.Scaling);
        window.Position = new PixelPoint(
            screen.WorkingArea.Right - width - ScreenMarginPx,
            screen.WorkingArea.Bottom - height - ScreenMarginPx);
    }

    private void OnWindowPointerEntered(object? sender, PointerEventArgs args)
    {
        if (_state.Pause(DateTimeOffset.UtcNow))
        {
            _timer.Stop();
            return;
        }

        if (_state.Current is null)
        {
            _timer.Stop();
            _window?.Hide();
        }
    }

    private void OnWindowPointerExited(object? sender, PointerEventArgs args)
    {
        var remaining = _state.Resume(DateTimeOffset.UtcNow);
        if (remaining is null)
        {
            return;
        }

        _timer.Stop();
        _timer.Interval = remaining.Value;
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs args)
    {
        var now = DateTimeOffset.UtcNow;
        if (!_state.Expire(now))
        {
            if (!_state.IsPaused && _state.Current is { } current)
            {
                var remaining = current.ExpiresAt - now;
                if (remaining > TimeSpan.Zero)
                {
                    _timer.Stop();
                    _timer.Interval = remaining;
                    _timer.Start();
                }
            }

            return;
        }

        _timer.Stop();
        _window?.Hide();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        if (_window is not null)
        {
            _window.Clicked -= OnWindowClicked;
            _window.PointerEntered -= OnWindowPointerEntered;
            _window.PointerExited -= OnWindowPointerExited;
            _window.Close();
        }
        _window = null;
        GC.SuppressFinalize(this);
    }

    private sealed class NotificationWindow : Window
    {
        private readonly Border _surface;
        private readonly Border _iconBadge;
        private readonly TextBlock _icon;
        private readonly TextBlock _title;
        private readonly TextBlock _message;
        private UserNotificationSeverity _severity;

        public event EventHandler? Clicked;

        public NotificationWindow()
        {
            Width = WindowWidth;
            Height = WindowHeight;
            CanResize = false;
            ShowActivated = false;
            ShowInTaskbar = false;
            Topmost = true;
            SystemDecorations = SystemDecorations.None;
            Background = Brushes.Transparent;
            TransparencyBackgroundFallback = Brushes.Transparent;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            _icon = new TextBlock
            {
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _iconBadge = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = _icon
            };
            _title = new TextBlock
            {
                Text = "GhostSlacking",
                FontSize = 14,
                FontWeight = FontWeight.SemiBold
            };
            _message = new TextBlock
            {
                FontSize = 13,
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            copy.Children.Add(_title);
            copy.Children.Add(_message);

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            content.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            content.Children.Add(_iconBadge);
            Grid.SetColumn(copy, 1);
            content.Children.Add(copy);

            _surface = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                ClipToBounds = true,
                Margin = new Thickness(1),
                Padding = new Thickness(16, 14),
                Child = content
            };
            _surface.PointerPressed += OnPointerPressed;
            Content = _surface;
            ActualThemeVariantChanged += (_, _) => ApplyPalette();
            ApplyPalette();
        }

        public void Update(string message, UserNotificationSeverity severity, bool clickable)
        {
            _severity = severity;
            _message.Text = message;
            _surface.Cursor = clickable ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            ApplyPalette();
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
        {
            if (args.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            {
                return;
            }

            args.Handled = true;
            Clicked?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyPalette()
        {
            var palette = ResolvePalette(ActualThemeVariant, _severity);
            var backgroundBrush = new SolidColorBrush(palette.Surface);
            _surface.Background = backgroundBrush;
            _surface.BorderBrush = new SolidColorBrush(palette.Border);
            _iconBadge.Background = new SolidColorBrush(palette.Badge);
            _icon.Foreground = new SolidColorBrush(palette.Accent);
            _icon.Text = _severity == UserNotificationSeverity.Error ? "!" : "i";
            _title.Foreground = new SolidColorBrush(palette.Title);
            _message.Foreground = new SolidColorBrush(palette.Message);
        }
    }
}
