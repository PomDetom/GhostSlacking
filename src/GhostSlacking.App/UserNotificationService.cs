using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
    void Show(string message, UserNotificationSeverity severity);
}

internal readonly record struct TransientNotification(
    string Message,
    UserNotificationSeverity Severity,
    DateTimeOffset ExpiresAt);

internal sealed class TransientNotificationState(TimeSpan duration)
{
    public TransientNotification? Current { get; private set; }

    public void Show(string message, UserNotificationSeverity severity, DateTimeOffset now)
    {
        Current = new TransientNotification(message, severity, now.Add(duration));
    }

    public bool Expire(DateTimeOffset now)
    {
        if (Current is null || now < Current.Value.ExpiresAt)
        {
            return false;
        }

        Current = null;
        return true;
    }
}

internal sealed class AvaloniaNotificationService : IUserNotificationService
{
    internal static readonly TimeSpan DisplayDuration = TimeSpan.FromMilliseconds(2500);
    private const double WindowWidth = 360;
    private const double WindowHeight = 116;
    private const int ScreenMarginPx = 16;

    private readonly Func<DrawingPoint> _getCursorPosition;
    private readonly ILogger _logger;
    private readonly DispatcherTimer _timer;
    private readonly TransientNotificationState _state = new(DisplayDuration);
    private NotificationWindow? _window;
    private bool _disposed;

    public AvaloniaNotificationService(Func<DrawingPoint> getCursorPosition, ILogger logger)
    {
        _getCursorPosition = getCursorPosition;
        _logger = logger;
        _timer = new DispatcherTimer { Interval = DisplayDuration };
        _timer.Tick += OnTimerTick;
    }

    public void Show(string message, UserNotificationSeverity severity)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(message, severity));
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            _state.Show(message, severity, now);
            _window ??= new NotificationWindow();
            _window.Update(message, severity);
            PositionWindow(_window, _getCursorPosition());
            if (!_window.IsVisible)
            {
                _window.Show();
            }

            _timer.Stop();
            _timer.Start();
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "Avalonia notification could not be shown.", exception);
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

    private void OnTimerTick(object? sender, EventArgs args)
    {
        if (!_state.Expire(DateTimeOffset.UtcNow))
        {
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
        _window?.Close();
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

        public NotificationWindow()
        {
            Width = WindowWidth;
            Height = WindowHeight;
            CanResize = false;
            ShowActivated = false;
            ShowInTaskbar = false;
            Topmost = true;
            SystemDecorations = SystemDecorations.None;
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
                Padding = new Thickness(16, 14),
                Child = content
            };
            Content = _surface;
            ActualThemeVariantChanged += (_, _) => ApplyPalette();
            ApplyPalette();
        }

        public void Update(string message, UserNotificationSeverity severity)
        {
            _severity = severity;
            _message.Text = message;
            ApplyPalette();
        }

        private void ApplyPalette()
        {
            var dark = AppTheme.IsDark(ActualThemeVariant);
            var error = _severity == UserNotificationSeverity.Error;
            var background = Color.Parse(dark ? "#0A0A0A" : "#FFFFFF");
            var border = error ? Color.Parse("#EF4444") : AppTheme.AccentColor;
            var title = Color.Parse(dark ? "#F5F5F5" : "#172033");
            var message = Color.Parse(dark ? "#A3A3A3" : "#667085");
            var badge = Color.Parse(error
                ? dark ? "#3F1010" : "#FEE2E2"
                : dark ? "#103B37" : "#DDF7F5");
            var accent = error ? Color.Parse("#EF4444") : AppTheme.AccentColor;

            var backgroundBrush = new SolidColorBrush(background);
            Background = backgroundBrush;
            _surface.Background = backgroundBrush;
            _surface.BorderBrush = new SolidColorBrush(border);
            _iconBadge.Background = new SolidColorBrush(badge);
            _icon.Foreground = new SolidColorBrush(accent);
            _icon.Text = error ? "!" : "i";
            _title.Foreground = new SolidColorBrush(title);
            _message.Foreground = new SolidColorBrush(message);
        }
    }
}
