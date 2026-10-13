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

internal sealed class ChatNotificationEntry(
    Guid id,
    string message,
    UserNotificationSeverity severity,
    UserNotificationSource source,
    Action? clickAction)
{
    public Guid Id { get; } = id;
    public string Message { get; } = message;
    public UserNotificationSeverity Severity { get; } = severity;
    public UserNotificationSource Source { get; } = source;
    public Action? ClickAction { get; private set; } = clickAction;

    public Action? TakeClickAction()
    {
        var action = ClickAction;
        ClickAction = null;
        return action;
    }
}

internal sealed class ChatNotificationQueue
{
    public const int MaximumHistory = 5;
    public static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(5);
    private readonly List<ChatNotificationEntry> _entries = [];
    private DateTimeOffset? _visibleUntil;
    private TimeSpan? _pausedRemaining;

    public IReadOnlyList<ChatNotificationEntry> Entries => _entries;
    public bool IsVisible => _visibleUntil is not null;
    public bool IsPaused => _pausedRemaining is not null;
    public DateTimeOffset? VisibleUntil => _visibleUntil;

    public ChatNotificationEntry? Add(
        string message,
        UserNotificationSeverity severity,
        DateTimeOffset now,
        Action? clickAction = null,
        UserNotificationSource source = UserNotificationSource.General)
    {
        HideIfExpired(now);
        if (IsVisible && _entries.LastOrDefault() is { } latest &&
            UserNotificationPolicy.Priority(source) < UserNotificationPolicy.Priority(latest.Source))
        {
            return null;
        }

        var entry = new ChatNotificationEntry(Guid.NewGuid(), message, severity, source, clickAction);
        _entries.Add(entry);
        if (_entries.Count > MaximumHistory)
        {
            _entries.RemoveAt(0);
        }

        _visibleUntil = now.Add(DisplayDuration);
        _pausedRemaining = null;
        return entry;
    }

    public bool HideIfExpired(DateTimeOffset now)
    {
        if (!IsVisible || IsPaused || now < _visibleUntil)
        {
            return false;
        }

        Hide();
        return true;
    }

    public bool Pause(Guid id, DateTimeOffset now)
    {
        var entry = Find(id);
        if (entry?.ClickAction is null || IsPaused || !IsVisible)
        {
            return false;
        }

        var remaining = _visibleUntil!.Value - now;
        if (remaining <= TimeSpan.Zero)
        {
            Hide();
            return false;
        }

        _pausedRemaining = remaining;
        return true;
    }

    public TimeSpan? Resume(Guid id, DateTimeOffset now)
    {
        if (Find(id) is null || _pausedRemaining is not { } remaining)
        {
            return null;
        }

        _visibleUntil = now.Add(remaining);
        _pausedRemaining = null;
        return remaining;
    }

    public Action? TakeClickAction(Guid id)
    {
        var entry = Find(id);
        if (entry is null)
        {
            return null;
        }

        var action = entry.TakeClickAction();
        if (action is not null)
        {
            Hide();
        }

        return action;
    }

    public void Hide()
    {
        _visibleUntil = null;
        _pausedRemaining = null;
    }

    public void Clear()
    {
        Hide();
        _entries.Clear();
    }

    private ChatNotificationEntry? Find(Guid id) => _entries.FirstOrDefault(entry => entry.Id == id);
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

internal static class NotificationLayout
{
    private const int ScreenMarginPx = 16;

    public static IReadOnlyList<int> VisibleChatRows(
        IReadOnlyList<int> rowHeights,
        int availableHeight,
        int gap)
    {
        var visible = new List<int>();
        var usedHeight = 0;
        for (var index = rowHeights.Count - 1; index >= 0; index--)
        {
            var nextHeight = usedHeight + rowHeights[index] + (visible.Count == 0 ? 0 : gap);
            if (visible.Count > 0 && nextHeight > availableHeight)
            {
                break;
            }

            visible.Add(index);
            usedHeight = nextHeight;
        }

        visible.Reverse();
        return visible;
    }

    public static PixelPoint Position(
        PixelRect workingArea,
        double scaling,
        Size logicalSize,
        NotificationPlacement placement)
    {
        var normalized = placement.Normalize();
        var width = (int)Math.Ceiling(logicalSize.Width * scaling);
        var height = (int)Math.Ceiling(logicalSize.Height * scaling);
        var travelX = Math.Max(0, workingArea.Width - width - (2 * ScreenMarginPx));
        var travelY = Math.Max(0, workingArea.Height - height - (2 * ScreenMarginPx));
        return new PixelPoint(
            workingArea.X + ScreenMarginPx + (int)Math.Round(normalized.X * travelX),
            workingArea.Y + ScreenMarginPx + (int)Math.Round(normalized.Y * travelY));
    }

    public static NotificationPlacement FromPosition(
        PixelRect workingArea,
        double scaling,
        Size logicalSize,
        PixelPoint position)
    {
        var width = (int)Math.Ceiling(logicalSize.Width * scaling);
        var height = (int)Math.Ceiling(logicalSize.Height * scaling);
        var travelX = Math.Max(0, workingArea.Width - width - (2 * ScreenMarginPx));
        var travelY = Math.Max(0, workingArea.Height - height - (2 * ScreenMarginPx));
        return new NotificationPlacement(
            travelX == 0 ? 0 : (double)(position.X - workingArea.X - ScreenMarginPx) / travelX,
            travelY == 0 ? 0 : (double)(position.Y - workingArea.Y - ScreenMarginPx) / travelY).Normalize();
    }
}

internal sealed class AvaloniaNotificationService : IUserNotificationService
{
    internal static readonly TimeSpan DisplayDuration = TimeSpan.FromMilliseconds(2500);
    internal static readonly TimeSpan ClickableDisplayDuration = TimeSpan.FromMilliseconds(4500);
    internal static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(150);
    internal static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(200);

    private readonly Func<DrawingPoint> _getCursorPosition;
    private readonly Func<nint, NativeResult>? _bringToFront;
    private readonly Func<nint, bool, NativeResult>? _setClickThrough;
    private readonly ILogger _logger;
    private readonly DispatcherTimer _timer;
    private readonly TransientNotificationState _state = new(DisplayDuration);
    private readonly ChatNotificationQueue _chat = new();
    private readonly Dictionary<Guid, NotificationWindow> _chatWindows = [];
    private readonly HashSet<Guid> _hoveredChatRows = [];
    private AppSettings _settings;
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
        if (_disposed)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Configure(settings));
            return;
        }

        var normalized = settings.Normalize();
        if (normalized.NotificationStyle != _settings.NotificationStyle)
        {
            _state.Clear();
            _chat.Hide();
            _window?.Hide();
            CloseChatWindows();
            _timer.Stop();
        }

        _settings = normalized;
        if (_window?.IsVisible == true)
        {
            PositionWindow(_window, ActivePlacement(), _getCursorPosition());
        }

        if (_chat.IsVisible)
        {
            SyncChatWindows(DateTimeOffset.UtcNow);
        }
    }

    public void Show(
        string message,
        UserNotificationSeverity severity,
        Action? clickAction = null,
        UserNotificationSource source = UserNotificationSource.General)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(message, severity, clickAction, source));
            return;
        }

        try
        {
            if (_settings.NotificationStyle == NotificationStyle.Chat)
            {
                ShowChat(message, severity, clickAction, source);
            }
            else
            {
                ShowSingle(message, severity, clickAction, source);
            }
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "Avalonia notification could not be shown.", exception);
        }
    }

    private void ShowSingle(
        string message,
        UserNotificationSeverity severity,
        Action? clickAction,
        UserNotificationSource source)
    {
        var now = DateTimeOffset.UtcNow;
        if (_state.Current is { } current &&
            UserNotificationPolicy.ShouldSuppress(source, current, _state.IsPaused, now))
        {
            _logger.Log(LogLevel.Debug, $"NotificationSuppressed source={source} current={current.Source}");
            return;
        }

        var duration = clickAction is null ? DisplayDuration : ClickableDisplayDuration;
        _state.Show(message, severity, now, clickAction, duration, source);
        if (_window is null || _window.Style != _settings.NotificationStyle)
        {
            _window?.Close();
            _window = new NotificationWindow(_settings.NotificationStyle);
            _window.Clicked += OnWindowClicked;
            _window.PointerEntered += OnWindowPointerEntered;
            _window.PointerExited += OnWindowPointerExited;
        }

        var wasVisible = _window.IsVisible;
        var wasFadingOut = wasVisible && _window.IsFadingOut;
        _window.Update(message, severity, clickAction is not null);
        PositionWindow(_window, ActivePlacement(), _getCursorPosition());
        if (_window.Style != NotificationStyle.Card || !wasVisible || wasFadingOut)
        {
            _window.BeginFadeIn(now, reset: !wasFadingOut);
        }

        if (!wasVisible)
        {
            _window.Show();
        }

        RaiseWithoutActivation(_window, source);
        _timer.Start();
        if (clickAction is not null && _window.IsPointerOver)
        {
            _state.Pause(now);
        }
    }

    private void ShowChat(
        string message,
        UserNotificationSeverity severity,
        Action? clickAction,
        UserNotificationSource source)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = _chat.Add(message, severity, now, clickAction, source);
        if (entry is null)
        {
            _logger.Log(LogLevel.Debug, $"NotificationSuppressed source={source} current=chat");
            return;
        }

        SyncChatWindows(now);
        _timer.Start();
        foreach (var item in _chat.Entries)
        {
            if (item.ClickAction is not null && _chatWindows[item.Id].IsPointerOver)
            {
                OnChatPointerEntered(item.Id);
            }
        }
    }

    private NotificationPlacement ActivePlacement() => _settings.NotificationStyle switch
    {
        NotificationStyle.Capsule => _settings.CapsuleNotificationPlacement,
        NotificationStyle.Chat => _settings.ChatNotificationPlacement,
        _ => _settings.CardNotificationPlacement
    };

    private static void PositionWindow(Window window, NotificationPlacement placement, DrawingPoint cursor)
    {
        var screen = window.Screens.ScreenFromPoint(new PixelPoint(cursor.X, cursor.Y)) ?? window.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        window.Position = NotificationLayout.Position(
            screen.WorkingArea,
            screen.Scaling,
            new Size(window.Width, window.Height),
            placement);
    }

    private void SyncChatWindows(DateTimeOffset now)
    {
        var visibleIds = _chat.Entries.Select(entry => entry.Id).ToHashSet();
        foreach (var (id, window) in _chatWindows.ToArray())
        {
            if (visibleIds.Contains(id))
            {
                continue;
            }

            window.Close();
            _chatWindows.Remove(id);
        }

        foreach (var entry in _chat.Entries)
        {
            if (_chatWindows.ContainsKey(entry.Id))
            {
                continue;
            }

            var window = new NotificationWindow(NotificationStyle.Chat);
            var id = entry.Id;
            window.Clicked += (_, _) => OnChatClicked(id);
            window.PointerEntered += (_, _) => OnChatPointerEntered(id);
            window.PointerExited += (_, _) => OnChatPointerExited(id);
            _chatWindows.Add(id, window);
        }

        var displayedIds = PositionChatWindows();
        foreach (var entry in _chat.Entries)
        {
            var window = _chatWindows[entry.Id];
            if (!displayedIds.Contains(entry.Id))
            {
                if (_hoveredChatRows.Remove(entry.Id) && _hoveredChatRows.Count == 0)
                {
                    _chat.Resume(entry.Id, now);
                }

                window.Hide();
                continue;
            }

            window.ShowImmediately(now);

            if (!window.IsVisible)
            {
                window.Show();
            }

            SetChatClickThrough(window, entry.ClickAction is null);
            RaiseWithoutActivation(window, entry.Source);
        }
    }

    private HashSet<Guid> PositionChatWindows()
    {
        if (_chat.Entries.Count == 0)
        {
            return [];
        }

        var first = _chatWindows[_chat.Entries[0].Id];
        var cursor = _getCursorPosition();
        var screen = first.Screens.ScreenFromPoint(new PixelPoint(cursor.X, cursor.Y)) ?? first.Screens.Primary;
        if (screen is null)
        {
            return _chat.Entries.Select(entry => entry.Id).ToHashSet();
        }

        const double gap = 4;
        const int screenMargin = 16;
        var scaling = screen.Scaling;
        var availableHeightPx = Math.Max(1, screen.WorkingArea.Height - (2 * screenMargin));
        var availableHeight = availableHeightPx / scaling;
        var maximumTextWidth = Math.Max(1, Math.Min(600,
            ((screen.WorkingArea.Width - (2 * screenMargin)) / scaling) - 6));
        foreach (var entry in _chat.Entries)
        {
            _chatWindows[entry.Id].UpdateChat(
                entry.Message,
                entry.Severity,
                entry.ClickAction is not null,
                availableHeight,
                maximumTextWidth);
        }

        var gapPx = (int)Math.Ceiling(gap * scaling);
        var rowHeights = _chat.Entries
            .Select(entry => (int)Math.Ceiling(_chatWindows[entry.Id].Height * scaling))
            .ToArray();
        var visibleIndices = NotificationLayout.VisibleChatRows(rowHeights, availableHeightPx, gapPx);
        var displayedIds = visibleIndices.Select(index => _chat.Entries[index].Id).ToHashSet();
        var width = visibleIndices.Max(index => _chatWindows[_chat.Entries[index].Id].Width);
        var heightPx = visibleIndices.Sum(index => rowHeights[index]) +
            (gapPx * (visibleIndices.Count - 1));
        var height = heightPx / scaling;
        var placement = _settings.ChatNotificationPlacement.Normalize();
        var origin = NotificationLayout.Position(screen.WorkingArea, scaling, new Size(width, height), placement);
        var originY = heightPx > availableHeightPx
            ? screen.WorkingArea.Y + screenMargin +
                (int)Math.Round((availableHeightPx - heightPx) * placement.Y)
            : origin.Y;
        var bottom = originY + heightPx;
        for (var position = visibleIndices.Count - 1; position >= 0; position--)
        {
            var index = visibleIndices[position];
            var window = _chatWindows[_chat.Entries[index].Id];
            var rowHeight = rowHeights[index];
            bottom -= rowHeight;
            window.Position = new PixelPoint(
                origin.X + (int)Math.Round((width - window.Width) * scaling * placement.X),
                bottom);
            bottom -= gapPx;
        }

        return displayedIds;
    }

    private void SetChatClickThrough(NotificationWindow window, bool enabled)
    {
        if (_setClickThrough is null)
        {
            return;
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        var result = _setClickThrough(handle, enabled);
        if (!result.Success)
        {
            _logger.Log(LogLevel.Warning, $"NotificationClickThroughFailed error={result.ErrorCode} {result.ErrorMessage}");
        }
    }

    private void RaiseWithoutActivation(NotificationWindow window, UserNotificationSource source)
    {
        if (_bringToFront is null)
        {
            return;
        }

        var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (handle == 0)
        {
            _logger.Log(LogLevel.Warning, $"NotificationRaiseSkipped source={source} reason=no-platform-handle");
            return;
        }

        var result = _bringToFront(handle);
        if (!result.Success)
        {
            _logger.Log(LogLevel.Warning, $"NotificationRaiseFailed source={source} error={result.ErrorCode} {result.ErrorMessage}");
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

            _window?.BeginFadeOut(DateTimeOffset.UtcNow, FadeOutDuration);
            _timer.Start();
            action();
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "Notification click action failed.", exception);
        }
    }

    private void OnChatClicked(Guid id)
    {
        try
        {
            if (_chat.HideIfExpired(DateTimeOffset.UtcNow))
            {
                HideChatWindows();
                _timer.Stop();
                return;
            }

            var action = _chat.TakeClickAction(id);
            if (action is null)
            {
                return;
            }

            BeginChatFadeOut(DateTimeOffset.UtcNow, FadeOutDuration, disableInteraction: true);
            _timer.Start();
            action();
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "Notification click action failed.", exception);
        }
    }

    private void OnChatPointerEntered(Guid id)
    {
        if (_chat.Entries.All(entry => entry.Id != id || entry.ClickAction is null))
        {
            return;
        }

        _hoveredChatRows.Add(id);
        var now = DateTimeOffset.UtcNow;
        if (_chat.Pause(id, now))
        {
            foreach (var window in _chatWindows.Values)
            {
                window.ShowImmediately(now);
            }
        }
        else if (!_chat.IsVisible)
        {
            HideChatWindows();
            _timer.Stop();
        }
    }

    private void OnChatPointerExited(Guid id)
    {
        _hoveredChatRows.Remove(id);
        if (_hoveredChatRows.Count == 0)
        {
            _chat.Resume(id, DateTimeOffset.UtcNow);
        }
    }

    private void OnWindowPointerEntered(object? sender, PointerEventArgs args)
    {
        var now = DateTimeOffset.UtcNow;
        if (_state.Pause(now))
        {
            _window?.BeginFadeIn(now);
        }
        else if (_state.Expire(now))
        {
            _window?.Hide();
            _timer.Stop();
        }
    }

    private void OnWindowPointerExited(object? sender, PointerEventArgs args) =>
        _state.Resume(DateTimeOffset.UtcNow);

    private void OnTimerTick(object? sender, EventArgs args)
    {
        var now = DateTimeOffset.UtcNow;
        if (_settings.NotificationStyle == NotificationStyle.Chat)
        {
            if (_chat.IsVisible && !_chat.IsPaused &&
                _chat.VisibleUntil is { } visibleUntil &&
                now >= visibleUntil - FadeOutDuration)
            {
                BeginChatFadeOut(now, visibleUntil - now);
            }

            foreach (var window in _chatWindows.Values)
            {
                window.AdvanceFade(now);
            }

            if (_chat.HideIfExpired(now))
            {
                HideChatWindows();
            }

            if (!_chat.IsVisible && _chatWindows.Values.All(window => !window.IsVisible))
            {
                _timer.Stop();
            }

            return;
        }

        if (_state.Current is { } current && !_state.IsPaused &&
            now >= current.ExpiresAt - FadeOutDuration)
        {
            _window?.BeginFadeOut(now, current.ExpiresAt - now);
        }

        _window?.AdvanceFade(now);
        if (_state.Expire(now))
        {
            _window?.Hide();
        }

        if (_state.Current is null && _window?.IsVisible != true)
        {
            _timer.Stop();
        }
    }

    private void BeginChatFadeOut(DateTimeOffset now, TimeSpan duration, bool disableInteraction = false)
    {
        if (disableInteraction)
        {
            _hoveredChatRows.Clear();
        }

        foreach (var window in _chatWindows.Values)
        {
            if (!window.IsVisible)
            {
                continue;
            }

            window.BeginFadeOut(now, duration);
            if (disableInteraction)
            {
                SetChatClickThrough(window, true);
            }
        }
    }

    private void CloseChatWindows()
    {
        _hoveredChatRows.Clear();
        foreach (var window in _chatWindows.Values)
        {
            window.Close();
        }

        _chatWindows.Clear();
    }

    private void HideChatWindows()
    {
        _hoveredChatRows.Clear();
        foreach (var window in _chatWindows.Values)
        {
            window.Hide();
        }
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
        CloseChatWindows();
        GC.SuppressFinalize(this);
    }
}

internal sealed class NotificationWindow : Window
{
    private readonly Border _surface;
    private readonly Border? _accent;
    private readonly Border? _cardBody;
    private readonly TextBlock _message;
    private readonly TextBlock? _shadow;
    private readonly bool _preview;
    private readonly NotificationOpacityTransition _fade = new();
    private UserNotificationSeverity _severity;
    private bool _clickable;

    public NotificationStyle Style { get; }
    public bool IsFadingOut => _fade.IsFadingOut;
    public event EventHandler? Clicked;

    public NotificationWindow(NotificationStyle style, bool preview = false)
    {
        Style = style;
        _preview = preview;
        CanResize = false;
        ShowActivated = preview;
        ShowInTaskbar = false;
        Topmost = true;
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyBackgroundFallback = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _message = new TextBlock
        {
            FontSize = style == NotificationStyle.Chat ? 14 : 13,
            FontWeight = style == NotificationStyle.Chat ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = style == NotificationStyle.Capsule ? TextWrapping.NoWrap : TextWrapping.Wrap,
            MaxWidth = style switch
            {
                NotificationStyle.Capsule => 290,
                NotificationStyle.Chat => 400,
                _ => 330
            }
        };

        if (style == NotificationStyle.Card)
        {
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(4)));
            content.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            _accent = new Border
            {
                Width = 4,
                CornerRadius = new CornerRadius(8, 0, 0, 8)
            };
            content.Children.Add(_accent);
            _message.Margin = new Thickness(11, 8, 11, 8);
            _cardBody = new Border
            {
                BorderThickness = new Thickness(0, 1, 1, 1),
                Child = _message
            };
            Grid.SetColumn(_cardBody, 1);
            content.Children.Add(_cardBody);
            _surface = new Border
            {
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Child = content
            };
        }
        else if (style == NotificationStyle.Capsule)
        {
            _surface = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                ClipToBounds = true,
                Margin = new Thickness(1),
                Padding = new Thickness(13, 8),
                Child = _message
            };
        }
        else
        {
            var text = new Grid();
            _shadow = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 400,
                RenderTransform = new TranslateTransform(1.5, 1.5),
                Foreground = new SolidColorBrush(Color.Parse("#D0000000")),
                IsHitTestVisible = false
            };
            RenderOptions.SetTextRenderingMode(_shadow, TextRenderingMode.Antialias);
            text.Children.Add(_shadow);
            RenderOptions.SetTextRenderingMode(_message, TextRenderingMode.Antialias);
            text.Children.Add(_message);
            _surface = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(3),
                Child = text
            };
        }

        _surface.PointerPressed += OnPointerPressed;
        Content = _surface;
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        ApplyPalette();
    }

    public void BeginFadeIn(DateTimeOffset now, bool reset = false)
    {
        if (_preview)
        {
            return;
        }

        _fade.Start(1, now, AvaloniaNotificationService.FadeInDuration, reset);
        Opacity = _fade.Opacity;
    }

    public void ShowImmediately(DateTimeOffset now)
    {
        if (_preview)
        {
            return;
        }

        _fade.Start(1, now, TimeSpan.Zero);
        Opacity = 1;
    }

    public void BeginFadeOut(DateTimeOffset now, TimeSpan duration)
    {
        if (_preview || !IsVisible || _fade.IsFadingOut)
        {
            return;
        }

        _fade.Start(0, now, duration);
        Opacity = _fade.Opacity;
        if (!_fade.IsAnimating)
        {
            Hide();
        }
    }

    public void AdvanceFade(DateTimeOffset now)
    {
        if (_preview || !IsVisible)
        {
            return;
        }

        Opacity = _fade.Advance(now);
        if (!_fade.IsAnimating && Opacity == 0)
        {
            Hide();
        }
    }

    public void UpdateChat(
        string message,
        UserNotificationSeverity severity,
        bool clickable,
        double availableHeight,
        double maximumTextWidth)
    {
        var initialWidth = Math.Min(400, maximumTextWidth);
        Update(message, severity, clickable, initialWidth);
        if (Height <= availableHeight || maximumTextWidth <= initialWidth)
        {
            return;
        }

        var widestWidth = (int)Math.Floor(maximumTextWidth);
        Update(message, severity, clickable, widestWidth);
        if (Height > availableHeight)
        {
            return;
        }

        var low = (int)Math.Ceiling(initialWidth);
        var high = widestWidth;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            Update(message, severity, clickable, middle);
            if (Height <= availableHeight)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        Update(message, severity, clickable, low);
    }

    public void Update(
        string message,
        UserNotificationSeverity severity,
        bool clickable,
        double? chatTextWidth = null)
    {
        _severity = severity;
        _clickable = clickable;
        if (Style == NotificationStyle.Chat && chatTextWidth is { } width)
        {
            _message.MaxWidth = width;
            _shadow!.MaxWidth = width;
        }

        var displayedText = Style switch
        {
            NotificationStyle.Capsule => CapsuleTextBalancer.Arrange(message, _message.MaxWidth, MeasureUnwrapped).Text,
            NotificationStyle.Chat => ChatTextWrapper.BreakOversizedWords(message, _message.MaxWidth, MeasureUnwrapped),
            _ => message
        };
        _message.Text = displayedText;
        if (_shadow is not null)
        {
            _shadow.Text = displayedText;
        }

        _surface.Cursor = clickable || _preview ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        ApplyPalette();
        Size textSize;
        if (Style == NotificationStyle.Chat)
        {
            _message.Measure(new Size(_message.MaxWidth, double.PositiveInfinity));
            textSize = _message.DesiredSize;
        }
        else
        {
            var measurement = new TextBlock
            {
                Text = displayedText,
                FontSize = _message.FontSize,
                FontFamily = _message.FontFamily,
                FontWeight = _message.FontWeight,
                TextWrapping = _message.TextWrapping,
                MaxWidth = _message.MaxWidth
            };
            measurement.Measure(new Size(_message.MaxWidth, double.PositiveInfinity));
            textSize = measurement.DesiredSize;
        }
        Width = Math.Ceiling(textSize.Width + (Style switch
        {
            NotificationStyle.Card => 29,
            NotificationStyle.Capsule => 30,
            _ => 6
        }));
        Height = Math.Ceiling(textSize.Height + (Style == NotificationStyle.Chat ? 6 : 20));
    }

    private double MeasureUnwrapped(string value)
    {
        var measurement = new TextBlock
        {
            Text = value,
            FontSize = _message.FontSize,
            FontFamily = _message.FontFamily,
            FontWeight = _message.FontWeight,
            TextWrapping = TextWrapping.NoWrap
        };
        measurement.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return measurement.DesiredSize.Width;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (args.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        args.Handled = true;
        if (_preview)
        {
            BeginMoveDrag(args);
        }
        else if (_clickable)
        {
            Clicked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyPalette()
    {
        var palette = AvaloniaNotificationService.ResolvePalette(ActualThemeVariant, _severity);
        if (Style == NotificationStyle.Chat)
        {
            _message.Foreground = new SolidColorBrush(
                _severity == UserNotificationSeverity.Error ? Color.Parse("#FFB0A8") : Color.Parse("#FFFFFF"));
            return;
        }

        if (Style == NotificationStyle.Card)
        {
            _surface.Background = new SolidColorBrush(palette.Surface);
            _cardBody!.Background = new SolidColorBrush(palette.Surface);
            _cardBody.BorderBrush = new SolidColorBrush(Color.Parse(
                AppTheme.IsDark(ActualThemeVariant) ? "#3A3A3A" : "#E4E7EC"));
        }
        else
        {
            _surface.Background = new SolidColorBrush(palette.Surface);
            _surface.BorderBrush = new SolidColorBrush(palette.Border);
        }
        _message.Foreground = new SolidColorBrush(palette.Title);
        if (_accent is not null)
        {
            _accent.Background = new SolidColorBrush(palette.Accent);
        }
    }
}
