using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Styling;
using FluentAvalonia.Styling;

namespace GhostSlacking.App;

internal sealed class GhostSlackingApplication : Avalonia.Application
{
    private GhostApplicationController? _controller;

    public override void Initialize()
    {
        Styles.Add(new FluentAvaloniaTheme());
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
    }

    private void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs args)
    {
        _controller?.ReportUnhandledException(args.Exception);
        args.Handled = true;
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs args)
    {
        Dispatcher.UIThread.UnhandledException -= OnUnhandledException;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.Exit -= OnExit;
        }

        _controller?.Dispose();
        _controller = null;
    }
}
