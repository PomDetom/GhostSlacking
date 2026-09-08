using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal sealed class SettingsWindowHost : IDisposable
{
    private readonly object _gate = new();
    private readonly ILogger _logger;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly CancellationTokenSource _shutdown = new();
    private Thread? _thread;
    private SettingsWindow? _window;
    private Action<Exception?>? _closed;
    private Exception? _startupException;
    private bool _openRequested;
    private bool _disposed;

    public SettingsWindowHost(ILogger logger)
    {
        _logger = logger;
    }

    public bool Show(AppSettings settings, Func<AppSettings, bool> save, Action<Exception?> closed)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(closed);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_openRequested)
            {
                Activate();
                return true;
            }

            _openRequested = true;
            _closed = closed;
        }

        if (!EnsureStarted())
        {
            lock (_gate)
            {
                _openRequested = false;
                _closed = null;
            }

            return false;
        }

        Dispatcher.UIThread.Post(() => ShowCore(settings, save));
        return true;
    }

    public void Activate()
    {
        if (!_ready.IsSet || _startupException is not null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_window is null)
            {
                return;
            }

            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.Activate();
        });
    }

    private bool EnsureStarted()
    {
        lock (_gate)
        {
            if (_thread is null)
            {
                _thread = new Thread(ThreadMain)
                {
                    IsBackground = true,
                    Name = "GhostSlacking.Avalonia"
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
        }

        if (!_ready.Wait(TimeSpan.FromSeconds(15)))
        {
            _logger.Log(LogLevel.Error, "Timed out while starting the Avalonia settings host.");
            return false;
        }

        return _startupException is null;
    }

    private void ThreadMain()
    {
        try
        {
            AppBuilder.Configure<SettingsAvaloniaApplication>()
                .UsePlatformDetect()
                .SetupWithoutStarting();
            _ready.Set();
            Avalonia.Application.Current!.Run(_shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _startupException = exception;
            _logger.Log(LogLevel.Error, "The Avalonia settings host failed.", exception);
            _ready.Set();
            NotifyClosed(exception);
        }
    }

    private void ShowCore(AppSettings settings, Func<AppSettings, bool> save)
    {
        try
        {
            var window = new SettingsWindow(settings, save);
            _window = window;
            window.Closed += (_, _) =>
            {
                _window = null;
                NotifyClosed(null);
            };
            window.Show();
            window.Activate();
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Error, "The Avalonia settings window could not be shown.", exception);
            _window = null;
            NotifyClosed(exception);
        }
    }

    private void NotifyClosed(Exception? exception)
    {
        Action<Exception?>? callback;
        lock (_gate)
        {
            if (!_openRequested)
            {
                return;
            }

            _openRequested = false;
            callback = _closed;
            _closed = null;
        }

        callback?.Invoke(exception);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        if (_ready.IsSet && _startupException is null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _window?.Close();
                _shutdown.Cancel();
            });
        }
        else
        {
            _shutdown.Cancel();
        }

        if (_thread is not null && _thread != Thread.CurrentThread)
        {
            _thread.Join(TimeSpan.FromSeconds(3));
        }

        _shutdown.Dispose();
        _ready.Dispose();
        GC.SuppressFinalize(this);
    }
}
