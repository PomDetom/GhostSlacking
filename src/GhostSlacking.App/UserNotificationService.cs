using GhostSlacking.Core;
using Microsoft.Windows.ApplicationModel.DynamicDependency;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace GhostSlacking.App;

internal enum UserNotificationSeverity
{
    Info,
    Error
}

internal readonly record struct UserNotificationRequest(
    string Message,
    UserNotificationSeverity Severity,
    string Tag,
    string Group,
    DateTimeOffset Expiration);

internal interface IUserNotificationService : IDisposable
{
    void Show(string message, UserNotificationSeverity severity);
}

internal interface IUserNotificationFallback
{
    void Show(string message, UserNotificationSeverity severity);
}

internal interface IWindowsAppNotificationBackend : IDisposable
{
    bool TryInitialize(out string failureReason);
    bool CanShow(out string setting);
    void Show(UserNotificationRequest request);
}

internal sealed class WindowsAppNotificationService : IUserNotificationService
{
    internal const string TransientTag = "ghostslacking-transient";
    internal const string TransientGroup = "runtime";
    internal static readonly TimeSpan Expiration = TimeSpan.FromSeconds(30);

    private readonly IWindowsAppNotificationBackend _backend;
    private readonly IUserNotificationFallback _fallback;
    private readonly ILogger _logger;
    private bool _initialized;
    private bool _disposed;
    private string? _lastPrimaryFailure;
    private string? _lastFallbackFailure;

    private WindowsAppNotificationService(
        IWindowsAppNotificationBackend backend,
        IUserNotificationFallback fallback,
        ILogger logger)
    {
        _backend = backend;
        _fallback = fallback;
        _logger = logger;
    }

    public static WindowsAppNotificationService Create(NotifyIcon notifyIcon, ILogger logger)
    {
        var service = new WindowsAppNotificationService(
            new WindowsAppNotificationBackend(),
            new NotifyIconNotificationFallback(notifyIcon),
            logger);
        service.Initialize();
        return service;
    }

    internal static WindowsAppNotificationService CreateForTesting(
        IWindowsAppNotificationBackend backend,
        IUserNotificationFallback fallback,
        ILogger logger)
    {
        var service = new WindowsAppNotificationService(backend, fallback, logger);
        service.Initialize();
        return service;
    }

    public void Show(string message, UserNotificationSeverity severity)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (!_initialized)
        {
            ShowFallback(message, severity);
            return;
        }

        try
        {
            if (!_backend.CanShow(out var setting))
            {
                LogPrimaryFailureOnce($"Windows app notifications are disabled: {setting}.");
                return;
            }

            var request = new UserNotificationRequest(
                message,
                severity,
                TransientTag,
                TransientGroup,
                DateTimeOffset.Now.Add(Expiration));
            _backend.Show(request);
            _lastPrimaryFailure = null;
            _logger.Log(LogLevel.Debug, $"WindowsAppNotificationSent severity={severity}");
        }
        catch (Exception exception)
        {
            LogPrimaryFailureOnce("Windows app notification could not be sent.", exception);
            ShowFallback(message, severity);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _backend.Dispose();
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "Windows app notification resources could not be released cleanly.", exception);
        }
    }

    private void Initialize()
    {
        try
        {
            if (_backend.TryInitialize(out var failureReason))
            {
                _initialized = true;
                _logger.Log(LogLevel.Info, "Windows app notifications initialized.");
                return;
            }

            LogPrimaryFailureOnce($"Windows app notifications are unavailable: {failureReason}.");
        }
        catch (Exception exception)
        {
            LogPrimaryFailureOnce("Windows app notifications could not be initialized.", exception);
        }
    }

    private void LogPrimaryFailureOnce(string message, Exception? exception = null)
    {
        if (string.Equals(_lastPrimaryFailure, message, StringComparison.Ordinal))
        {
            return;
        }

        _lastPrimaryFailure = message;
        _logger.Log(LogLevel.Warning, message, exception);
    }

    private void ShowFallback(string message, UserNotificationSeverity severity)
    {
        try
        {
            _fallback.Show(message, severity);
            _lastFallbackFailure = null;
            _logger.Log(LogLevel.Debug, $"TrayNotificationSent severity={severity}");
        }
        catch (Exception exception)
        {
            const string failure = "Tray notification could not be sent.";
            if (string.Equals(_lastFallbackFailure, failure, StringComparison.Ordinal))
            {
                return;
            }

            _lastFallbackFailure = failure;
            _logger.Log(LogLevel.Warning, failure, exception);
        }
    }
}

internal sealed class NotifyIconNotificationFallback(NotifyIcon notifyIcon) : IUserNotificationFallback
{
    private const int DisplayDurationMilliseconds = 2500;

    public void Show(string message, UserNotificationSeverity severity)
    {
        var icon = severity == UserNotificationSeverity.Error
            ? ToolTipIcon.Error
            : ToolTipIcon.Info;
        notifyIcon.ShowBalloonTip(DisplayDurationMilliseconds, "GhostSlacking", message, icon);
    }
}

internal sealed class WindowsAppNotificationBackend : IWindowsAppNotificationBackend
{
    private const uint WindowsAppSdk18 = 0x00010008;

    private AppNotificationManager? _manager;
    private bool _bootstrapped;
    private bool _registered;
    private bool _disposed;

    public bool TryInitialize(out string failureReason)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            failureReason = "Windows 10 version 1809 or later is required";
            return false;
        }

        if (!Bootstrap.TryInitialize(WindowsAppSdk18, string.Empty, out var hresult))
        {
            failureReason = $"Windows App Runtime 1.8 bootstrap failed with HRESULT 0x{hresult:X8}";
            return false;
        }

        _bootstrapped = true;
        if (!AppNotificationManager.IsSupported())
        {
            failureReason = "AppNotificationManager is not supported by this Windows environment";
            return false;
        }

        _manager = AppNotificationManager.Default;
        _manager.NotificationInvoked += OnNotificationInvoked;
        try
        {
            _manager.Register();
            _registered = true;
            failureReason = string.Empty;
            return true;
        }
        catch
        {
            _manager.NotificationInvoked -= OnNotificationInvoked;
            _manager = null;
            throw;
        }
    }

    public bool CanShow(out string setting)
    {
        if (!_registered || _manager is null)
        {
            setting = "not registered";
            return false;
        }

        var current = _manager.Setting;
        setting = current.ToString();
        return current == AppNotificationSetting.Enabled;
    }

    public void Show(UserNotificationRequest request)
    {
        if (!_registered || _manager is null)
        {
            throw new InvalidOperationException("Windows app notifications are not registered.");
        }

        var notification = new AppNotificationBuilder()
            .AddText(request.Message)
            .MuteAudio()
            .SetDuration(AppNotificationDuration.Default)
            .BuildNotification();
        notification.Tag = request.Tag;
        notification.Group = request.Group;
        notification.Expiration = request.Expiration;
        _manager.Show(notification);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_manager is not null)
            {
                if (_registered)
                {
                    _manager.Unregister();
                }

                _manager.NotificationInvoked -= OnNotificationInvoked;
            }
        }
        finally
        {
            _registered = false;
            _manager = null;
            if (_bootstrapped)
            {
                Bootstrap.Shutdown();
                _bootstrapped = false;
            }
        }
    }

    private static void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        // Runtime notifications are informational and never open application UI.
    }
}
