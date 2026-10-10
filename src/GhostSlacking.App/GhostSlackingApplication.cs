using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Styling;
using FluentAvalonia.Styling;

namespace GhostSlacking.App;

internal sealed class GhostSlackingApplication : Avalonia.Application
{
    private static readonly TimeSpan StartupNotificationDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan UpdateCheckDelay = TimeSpan.FromSeconds(3.5);
    private GhostApplicationController? _controller;
    private IDisposable? _startupNotificationRegistration;
    private IDisposable? _updateCheckRegistration;
    private Task _startupHandshake = Task.CompletedTask;

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
            _startupHandshake = Task.Run(async () =>
            {
                try
                {
                    var args = lifetime.Args ?? [];
                    var confirmed = await GhostSlacking.Platform.UpdateStartupHandshake.ReportAsync(args, ApplicationVersionInfo.Current());
                    if (confirmed || !args.Contains("--update-session"))
                        GhostSlacking.Platform.UserInstallation.ConfirmStarted(AppContext.BaseDirectory);
                }
                catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
                { System.Diagnostics.Debug.WriteLine(exception); }
            });
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
            _updateCheckRegistration = DispatcherTimer.RunOnce(
                BeginAutomaticUpdateCheck,
                UpdateCheckDelay,
                DispatcherPriority.Background);
        }
    }

    private async void ShowStartupNotification()
    {
        _startupNotificationRegistration = null;
        await _startupHandshake;
        _controller?.ShowStartupNotification();
    }

    private void BeginAutomaticUpdateCheck()
    {
        _updateCheckRegistration = null;
        _controller?.BeginAutomaticUpdateCheck();
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
        _updateCheckRegistration?.Dispose();
        _updateCheckRegistration = null;
        Dispatcher.UIThread.UnhandledException -= OnUnhandledException;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.Exit -= OnExit;
        }

        _controller?.Dispose();
        _controller = null;
    }
}
