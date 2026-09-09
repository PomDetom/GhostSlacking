using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Styling;
using FluentAvalonia.Styling;

namespace GhostSlacking.App;

internal sealed class GhostSlackingApplication : Avalonia.Application
{
    private static readonly TimeSpan StartupNotificationDelay = TimeSpan.FromMilliseconds(500);
    private GhostApplicationController? _controller;
    private IDisposable? _startupNotificationRegistration;

    public override void Initialize()
    {
        Styles.Add(new FluentAvaloniaTheme
        {
            PreferSystemTheme = true,
            PreferUserAccentColor = false,
            CustomAccentColor = AppTheme.AccentColor
        });
        RequestedThemeVariant = ThemeVariant.Default;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            _controller = new GhostApplicationController(lifetime);
            lifetime.Exit += OnExit;
            Dispatcher.UIThread.UnhandledException += OnUnhandledException;
        }

        base.OnFrameworkInitializationCompleted();

        if (_controller is not null)
        {
            _startupNotificationRegistration = DispatcherTimer.RunOnce(
                ShowStartupNotification,
                StartupNotificationDelay,
                DispatcherPriority.Normal);
        }
    }

    private void ShowStartupNotification()
    {
        _startupNotificationRegistration = null;
        _controller?.ShowStartupNotification();
    }

    private void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs args)
    {
        _controller?.ReportUnhandledException(args.Exception);
        args.Handled = true;
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs args)
    {
        _startupNotificationRegistration?.Dispose();
        _startupNotificationRegistration = null;
        Dispatcher.UIThread.UnhandledException -= OnUnhandledException;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.Exit -= OnExit;
        }

        _controller?.Dispose();
        _controller = null;
    }
}
