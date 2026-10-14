using Avalonia.Media;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal enum UserNotificationSeverity
{
    Info,
    Error,
    Success
}

internal enum UserNotificationSource
{
    General,
    Startup,
    Update,
    Picker,
    Selection,
    Error
}

internal interface IUserNotificationService : IDisposable
{
    void Configure(AppSettings settings);

    void RefreshLayout();

    void Show(
        string message,
        UserNotificationSeverity severity,
        Action? clickAction = null,
        UserNotificationSource source = UserNotificationSource.General);
}

internal readonly record struct TransientNotification(
    string Message,
    UserNotificationSeverity Severity,
    DateTimeOffset ExpiresAt,
    Action? ClickAction,
    UserNotificationSource Source);

internal readonly record struct UserNotificationPalette(Color Surface, Color Border, Color Message, Color Icon);

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
        TimeSpan? displayDuration = null,
        UserNotificationSource source = UserNotificationSource.General)
    {
        var selectedDuration = displayDuration ?? duration;
        if (selectedDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(displayDuration));
        }

        _pausedRemaining = null;
        Current = new TransientNotification(message, severity, now.Add(selectedDuration), clickAction, source);
    }

    public bool Expire(DateTimeOffset now)
    {
        if (Current is null || IsPaused || now < Current.Value.ExpiresAt)
        {
            return false;
        }

        Clear();
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
            Clear();
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
        if (action is not null)
        {
            Clear();
        }

        return action;
    }

    public void Clear()
    {
        _pausedRemaining = null;
        Current = null;
    }
}

internal static class UserNotificationPolicy
{
    public static bool ShouldSuppress(
        UserNotificationSource source,
        TransientNotification current,
        bool isPaused,
        DateTimeOffset now)
    {
        if (!isPaused && now >= current.ExpiresAt)
        {
            return false;
        }

        return Priority(source) < Priority(current.Source);
    }

    public static int Priority(UserNotificationSource source) => source switch
    {
        UserNotificationSource.Error => 5,
        UserNotificationSource.Selection => 4,
        UserNotificationSource.Picker => 3,
        UserNotificationSource.Update => 2,
        UserNotificationSource.General => 2,
        UserNotificationSource.Startup => 1,
        _ => 0
    };
}

internal sealed class NotificationOpacityTransition
{
    private double _startOpacity = 1;
    private double _targetOpacity = 1;
    private DateTimeOffset _startedAt;
    private TimeSpan _duration;

    public double Opacity { get; private set; } = 1;
    public bool IsAnimating { get; private set; }
    public bool IsFadingOut => IsAnimating && _targetOpacity == 0;

    public void Start(double targetOpacity, DateTimeOffset now, TimeSpan duration, bool reset = false)
    {
        Advance(now);
        if (reset)
        {
            Opacity = 0;
        }

        _startOpacity = Opacity;
        _targetOpacity = targetOpacity;
        _startedAt = now;
        _duration = duration;
        IsAnimating = _startOpacity != _targetOpacity && duration > TimeSpan.Zero;
        if (!IsAnimating)
        {
            Opacity = targetOpacity;
        }
    }

    public double Advance(DateTimeOffset now)
    {
        if (!IsAnimating)
        {
            return Opacity;
        }

        var progress = Math.Clamp((now - _startedAt).TotalMilliseconds / _duration.TotalMilliseconds, 0, 1);
        var eased = _targetOpacity > _startOpacity
            ? 1 - Math.Pow(1 - progress, 2)
            : progress * progress;
        Opacity = _startOpacity + ((_targetOpacity - _startOpacity) * eased);
        if (progress >= 1)
        {
            Opacity = _targetOpacity;
            IsAnimating = false;
        }

        return Opacity;
    }
}
