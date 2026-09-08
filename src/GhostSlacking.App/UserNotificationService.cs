using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
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
        private readonly InfoBar _infoBar;

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
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            _infoBar = new InfoBar
            {
                Title = "GhostSlacking",
                IsOpen = true,
                IsClosable = false,
                Margin = new Thickness(8)
            };
            Content = _infoBar;
        }

        public void Update(string message, UserNotificationSeverity severity)
        {
            _infoBar.Message = message;
            _infoBar.Severity = severity == UserNotificationSeverity.Error
                ? InfoBarSeverity.Error
                : InfoBarSeverity.Informational;
            _infoBar.IsOpen = true;
        }
    }
}
