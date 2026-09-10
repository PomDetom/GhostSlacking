using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System.Diagnostics;
using System.Drawing;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App;

internal sealed class GhostApplicationController : IDisposable
{
    private const int WindowVisibilityHotkey = 1;
    private const int RestoreHotkey = 2;
    private const int EmergencyRestoreHotkey = 3;
    private const int SettingsHotkey = 4;
    private const int QuitHotkey = 5;
    private const int PickHotkey = 6;
    private const int DiameterIncreaseHotkey = 7;
    private const int DiameterDecreaseHotkey = 8;
    private readonly FileLogger _logger;
    private readonly DataExportService _dataExport;
    private readonly IApplicationUpdateManager _updates;
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly IUserNotificationService _notifications;
    private readonly SettingsStore _settingsStore;
    private readonly Win32WindowApi _windows;
    private readonly RecoveryManager _recovery;
    private readonly GhostCoordinator _coordinator;
    private readonly IWatchdogClient? _watchdog;
    private readonly Win32WindowPicker _picker;
    private readonly Win32MessageWindow _messageWindow;
    private readonly IDisplayRefreshRateProvider _displayRefreshRates;
    private readonly Win32HotkeyManager _hotkeys;
    private readonly LowLevelMouseHook _mouseHook;
    private readonly LowLevelKeyboardHook _keyboardHook;
    private readonly DispatcherTimer _timer;
    private readonly TrayIcon _trayIcon;
    private readonly RevealEdgeOverlay _revealOverlay;
    private readonly NativeMenuItem _statusItem;
    private readonly NativeMenuItem _pickItem;
    private readonly NativeMenuItem _toggleItem;
    private readonly NativeMenuItem _settingsItem;
    private readonly NativeMenuItem _updateItem;
    private readonly NativeMenuItem _exitItem;
    private readonly NativeMenuItem _restoreItem;
    private readonly NativeMenuItem _restoreAllItem;
    private AppSettings _settings;
    private bool _pickerActive;
    private readonly PickerEscapeGate _pickerEscapeGate = new();
    private bool _peekDown;
    private bool _isExiting;
    private bool _forceRestoreOnExit;
    private readonly PeekStateTracker _peekState = new();
    private readonly TrayDoubleClickDetector _trayDoubleClick = new(TimeSpan.FromMilliseconds(500));
    private readonly RevealPerformanceTracker _revealPerformance = new();
    private readonly RevealFrameRateScheduler _revealFrameRate = new();
    private readonly Stopwatch _revealPerformanceClock = Stopwatch.StartNew();
    private SettingsWindow? _settingsWindow;
    private readonly CancellationTokenSource _updateCancellation = new();
    private bool _disposed;

    public GhostApplicationController(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        AppTheme.Apply(_settings.ThemeMode);
        _logger = new FileLogger(_settings);
        _dataExport = new DataExportService(_logger);
        _updates = new GitHubApplicationUpdateManager(_logger);
        _windows = new Win32WindowApi();
        _revealOverlay = new RevealEdgeOverlay(_logger);
        var backend = new Win32VisibilityBackend(_logger);
        _recovery = new RecoveryManager(_windows, backend, _logger);
        var visibility = new VisibilityEngine(backend, _logger);
        _coordinator = new GhostCoordinator(_windows, _recovery, visibility, _logger, _revealOverlay);
        _displayRefreshRates = new Win32DisplayRefreshRateProvider();
        _messageWindow = new Win32MessageWindow();
        _messageWindow.HotkeyPressed += OnHotkeyPressed;
        _messageWindow.CloseRequested += OnCloseRequested;
        _messageWindow.DisplayConfigurationChanged += OnDisplayConfigurationChanged;
        _hotkeys = new Win32HotkeyManager(_messageWindow.Handle);
        _mouseHook = new LowLevelMouseHook();
        _mouseHook.LeftButtonDown += OnPickerClick;
        _keyboardHook = new LowLevelKeyboardHook();
        _keyboardHook.KeyStateChanged += OnPickerKeyStateChanged;
        _picker = new Win32WindowPicker((uint)Environment.ProcessId, () => [_messageWindow.Handle]);

        _statusItem = CreateMenuItem(UiText.Text(_settings.Language, "status"), null, false);
        _restoreItem = CreateMenuItem(UiText.Text(_settings.Language, "restore"), () => RestoreCurrent("tray"));
        _restoreAllItem = CreateMenuItem(UiText.Text(_settings.Language, "restoreAll"), () => RestoreAll("tray"));
        _pickItem = CreateMenuItem(UiText.Text(_settings.Language, "pick"), BeginPicking);
        _toggleItem = CreateMenuItem(UiText.Text(_settings.Language, "toggle"), ToggleWindowVisibility);
        _settingsItem = CreateMenuItem(UiText.Text(_settings.Language, "settings"), () => OpenSettings());
        _updateItem = CreateMenuItem(string.Empty, () => OpenSettings("about"));
        _updateItem.IsVisible = false;
        _exitItem = CreateMenuItem(UiText.Text(_settings.Language, "exit"), ExitApplication);
        var menu = new NativeMenu
        {
            Items =
            {
                _statusItem,
                new NativeMenuItemSeparator(),
                _pickItem,
                _toggleItem,
                _restoreItem,
                _restoreAllItem,
                _settingsItem,
                _updateItem,
                new NativeMenuItemSeparator(),
                _exitItem
            }
        };

        _trayIcon = new TrayIcon
        {
            Icon = AppIcon.Instance,
            ToolTipText = $"GhostSlacking - {UiText.State(_settings.Language, GhostState.Idle)}",
            IsVisible = true,
            Menu = menu
        };
        _trayIcon.Clicked += OnTrayIconClicked;
        _notifications = new AvaloniaNotificationService(_windows.GetCursorPosition, _logger);
        _updates.Changed += OnUpdateStateChanged;

        _coordinator.StateChanged += (_, _) => UpdateTrayStatus();
        _coordinator.UserErrorOccurred += (_, error) => ShowCoreError(error.Kind);
        _coordinator.TargetClosed += (_, _) => UpdateTrayStatus();
        _watchdog = TryStartWatchdog();
        _recovery.ProfilesChanged += OnRecoveryProfilesChanged;
        PublishRecoveryManifest();

        _timer = new DispatcherTimer(
            _revealFrameRate.Current.Interval,
            DispatcherPriority.Input,
            OnPeekTimerTick);
        _timer.Start();

        RegisterHotkeys();
        UpdateTrayStatus();
    }

    internal void ShowStartupNotification()
    {
        _logger.Log(LogLevel.Debug, "Startup notification requested.");
        ShowInfo(UiText.Text(_settings.Language, "startupReady"));
    }

    internal async void BeginAutomaticUpdateCheck()
    {
        try
        {
            var snapshot = await _updates.CheckAsync(_updateCancellation.Token);
            if (snapshot.Status == ApplicationUpdateStatus.Available && snapshot.Release is not null && !_isExiting)
            {
                await Dispatcher.UIThread.InvokeAsync(() => ShowInfo(string.Format(
                    UiText.Text(_settings.Language, "updateAvailableNotification"),
                    snapshot.Release.VersionText)));
            }
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested)
        {
        }
    }

    private void OnPeekTimerTick(object? sender, EventArgs args)
    {
        _peekDown = _peekState.Update(_windows.IsKeyDown(_settings.PeekVirtualKey), _settings.PeekTrigger);
        var cursor = _windows.GetCursorPosition();
        var startedAt = Stopwatch.GetTimestamp();
        _coordinator.UpdatePeek(cursor, _peekDown);
        var revealActive = _peekDown && _coordinator.State == GhostState.Reveal;
        UpdateRevealFrameRate(cursor, revealActive);
        var updateDuration = Stopwatch.GetElapsedTime(startedAt);

        if (_settings.MinimumLogLevel != LogLevel.Debug || !revealActive)
        {
            _revealPerformance.Reset();
            return;
        }

        var summary = _revealPerformance.Record(
            cursor,
            updateDuration,
            _revealPerformanceClock.Elapsed,
            _revealFrameRate.Current.TargetFramesPerSecond);
        if (summary is null)
        {
            return;
        }

        _logger.Log(
            LogLevel.Debug,
            $"RevealPerformance frames={summary.FrameCount} targetFps={summary.TargetFramesPerSecond} " +
            $"effectiveFps={summary.EffectiveFramesPerSecond:F1} " +
            $"averageMs={summary.AverageDuration.TotalMilliseconds:F2} p95Ms={summary.P95Duration.TotalMilliseconds:F2} " +
            $"maximumMs={summary.MaximumDuration.TotalMilliseconds:F2} overBudget={summary.OverBudgetCount}");
    }

    private void UpdateRevealFrameRate(Point cursor, bool revealActive)
    {
        var display = revealActive ? _displayRefreshRates.GetForPoint(cursor) : null;
        var transition = _revealFrameRate.Update(revealActive, _settings.PeekFrameRateLimit, display);
        if (transition.IntervalChanged)
        {
            _timer.Interval = transition.Decision.Interval;
        }

        if (!transition.ContextChanged)
        {
            return;
        }

        _revealPerformance.Reset();
        var detected = transition.Decision.DetectedRefreshRate?.ToString() ?? "unavailable";
        var displayId = transition.Decision.DisplayId ?? "unknown";
        _logger.Log(
            LogLevel.Debug,
            $"RevealFrameRate active={transition.Decision.IsRevealActive} " +
            $"targetFps={transition.Decision.TargetFramesPerSecond} detectedHz={detected} " +
            $"limit={transition.Decision.Limit} display={displayId} fallback={transition.Decision.UsesFallback}");
    }

    private void OnDisplayConfigurationChanged()
    {
        _displayRefreshRates.Invalidate();
        _logger.Log(LogLevel.Debug, "Display configuration changed; refresh-rate cache invalidated.");
    }

    private static NativeMenuItem CreateMenuItem(string header, Action? action, bool enabled = true)
    {
        var item = new NativeMenuItem(header) { IsEnabled = enabled };
        if (action is not null)
        {
            item.Click += (_, _) => action();
        }

        return item;
    }

    private void OnTrayIconClicked(object? sender, EventArgs args)
    {
        if (_trayDoubleClick.RegisterClick(DateTimeOffset.UtcNow))
        {
            BeginPicking();
        }
    }

    private void RegisterHotkeys()
    {
        (int Id, HotkeyBinding Binding)[] shortcuts =
        [
            (PickHotkey, _settings.PickHotkey),
            (WindowVisibilityHotkey, _settings.WindowToggleHotkey),
            (RestoreHotkey, _settings.RestoreHotkey),
            (EmergencyRestoreHotkey, _settings.RestoreAllHotkey),
            (SettingsHotkey, _settings.SettingsHotkey),
            (QuitHotkey, _settings.ExitHotkey),
            (DiameterIncreaseHotkey, _settings.RevealDiameterIncreaseHotkey),
            (DiameterDecreaseHotkey, _settings.RevealDiameterDecreaseHotkey)
        ];
        var duplicates = shortcuts
            .Where(shortcut => !shortcut.Binding.IsDisabled)
            .GroupBy(shortcut => shortcut.Binding)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        foreach (var shortcut in shortcuts)
        {
            if (shortcut.Binding.IsDisabled)
            {
                continue;
            }

            if (duplicates.Contains(shortcut.Binding))
            {
                _logger.Log(LogLevel.Warning, $"Duplicate hotkey was not registered: {UiText.ShortcutName(_settings.Language, shortcut.Binding)}");
                continue;
            }

            RegisterShortcut(shortcut.Id, shortcut.Binding);
        }
    }

    private void RegisterShortcut(int id, HotkeyBinding binding)
    {
        var modifiers = HotkeyModifiers.NoRepeat;
        if (binding.Modifiers.HasFlag(ShortcutModifiers.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (binding.Modifiers.HasFlag(ShortcutModifiers.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (binding.Modifiers.HasFlag(ShortcutModifiers.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        Register(id, modifiers, binding.VirtualKey, UiText.ShortcutName(_settings.Language, binding));
    }

    private void Register(int id, HotkeyModifiers modifiers, int key, string display)
    {
        if (!_hotkeys.Register(id, modifiers, key, out var error))
        {
            _logger.Log(LogLevel.Warning, $"Hotkey registration failed: {display}, error={error}");
        }
    }

    private void OnHotkeyPressed(int id)
    {
        switch (id)
        {
            case PickHotkey:
                BeginPicking();
                break;
            case WindowVisibilityHotkey:
                ToggleWindowVisibility();
                break;
            case RestoreHotkey:
                RestoreCurrent("hotkey");
                break;
            case EmergencyRestoreHotkey:
                RestoreAll("emergency hotkey");
                break;
            case SettingsHotkey:
                OpenSettings();
                break;
            case QuitHotkey:
                ExitApplication();
                break;
            case DiameterIncreaseHotkey:
                AdjustRevealDiameter(1);
                break;
            case DiameterDecreaseHotkey:
                AdjustRevealDiameter(-1);
                break;
        }
    }

    private void BeginPicking()
    {
        if (_pickerActive || _pickerEscapeGate.AwaitingRelease)
        {
            return;
        }

        if (!_coordinator.BeginPicking())
        {
            return;
        }

        var mouseStarted = _mouseHook.Start();
        var keyboardStarted = mouseStarted && _keyboardHook.Start();
        _pickerActive = mouseStarted && keyboardStarted;
        if (!_pickerActive)
        {
            CancelPicking();
            ShowError(UiText.Text(_settings.Language, "pickFailed"));
        }
        else
        {
            ShowInfo(UiText.Text(_settings.Language, "clickToPick"));
        }
    }

    private void OnPickerClick(object? sender, MouseButtonEventArgs args)
    {
        Dispatcher.UIThread.Post(() => FinishPicking(args.ScreenPoint));
    }

    private void OnPickerKeyStateChanged(object? sender, KeyStateChangedEventArgs args)
    {
        var decision = _pickerEscapeGate.Process(_pickerActive, args.VirtualKey, args.IsDown);
        if (!decision.Handled)
        {
            return;
        }

        args.Handled = true;
        if (decision.CancelPicking)
        {
            Dispatcher.UIThread.Post(() =>
            {
                CancelPicking(waitForEscapeRelease: true);
                ShowInfo(UiText.Text(_settings.Language, "pickCancelled"));
            });
        }
        else if (decision.ReleaseKeyboardHook)
        {
            Dispatcher.UIThread.Post(_keyboardHook.Stop);
        }
    }

    private void CancelPicking(bool waitForEscapeRelease = false)
    {
        _pickerActive = false;
        _mouseHook.Stop();
        if (!waitForEscapeRelease)
        {
            _pickerEscapeGate.Reset();
            _keyboardHook.Stop();
        }

        _coordinator.CancelPicking();
    }

    private void FinishPicking(Point screenPoint)
    {
        if (!_pickerActive)
        {
            return;
        }

        _pickerActive = false;
        _mouseHook.Stop();
        _keyboardHook.Stop();
        _pickerEscapeGate.Reset();
        var target = _picker.PickAt(screenPoint);
        if (target is null)
        {
            _coordinator.CancelPicking();
            ShowError(UiText.Text(_settings.Language, "noWindow"));
            return;
        }

        var settings = new RevealSettings
        {
            DiameterPx = _settings.RevealDiameterPx,
            SoftEdgeWidthPx = _settings.RevealSoftEdgeWidthPx,
            BlurLevel = _settings.RevealBlurLevel,
            Shape = _settings.RevealShape,
            Trigger = _settings.PeekTrigger
        };
        if (!_coordinator.SelectWindow(target, settings))
        {
            return;
        }

        ShowInfo(string.Format(
            UiText.Text(_settings.Language, "windowSelected"),
            target.ProcessName ?? (UiText.IsChinese(_settings.Language) ? "窗口" : "window"),
            UiText.PeekKeyName(_settings.Language, _settings.PeekVirtualKey)));
    }

    private void ToggleWindowVisibility()
    {
        if (_coordinator.CurrentProfile is null)
        {
            ShowError(UiText.Text(_settings.Language, "selectWindowFirst"));
            return;
        }

        _coordinator.ToggleWindowVisibility("window visibility hotkey");
    }

    private void RestoreCurrent(string reason)
    {
        _coordinator.RestoreCurrent(reason);
        UpdateTrayStatus();
    }

    private void RestoreAll(string reason)
    {
        _coordinator.RestoreAll(reason);
        UpdateTrayStatus();
    }

    private void OpenSettings(string? initialPage = null)
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            if (string.Equals(initialPage, "about", StringComparison.Ordinal))
            {
                _settingsWindow.NavigateToAbout();
            }

            _settingsWindow.Activate();
            return;
        }

        _hotkeys.UnregisterAll();
        try
        {
            var window = new SettingsWindow(
                _settings,
                SaveSettings,
                exportLogs: stream => _dataExport.ExportLogsAsync(stream, _settings.MinimumLogLevel),
                exportSettings: (settings, stream) => _dataExport.ExportSettingsAsync(stream, settings),
                updates: _updates,
                initialPage: initialPage);
            _settingsWindow = window;
            window.Closed += OnSettingsClosed;
            window.Show();
            window.Activate();
        }
        catch (Exception exception)
        {
            _settingsWindow = null;
            _logger.Log(LogLevel.Error, "The Avalonia settings window could not be shown.", exception);
            ShowError(UiText.Text(_settings.Language, "settingsOpenFailed"));
            if (!_isExiting)
            {
                RegisterHotkeys();
            }
        }
    }

    private void OnSettingsClosed(object? sender, EventArgs args)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Closed -= OnSettingsClosed;
            _settingsWindow = null;
        }

        if (!_isExiting)
        {
            RegisterHotkeys();
        }
    }

    private bool SaveSettings(AppSettings updated)
    {
        var previousKey = _settings.PeekVirtualKey;
        var previousTrigger = _settings.PeekTrigger;
        updated = updated.Normalize();
        if (!_settingsStore.Save(updated))
        {
            return false;
        }

        _settings = updated;
        _logger.MinimumLevel = _settings.MinimumLogLevel;
        AppTheme.Apply(_settings.ThemeMode);
        if (previousKey != _settings.PeekVirtualKey || previousTrigger != _settings.PeekTrigger)
        {
            _peekState.Reset(_windows.IsKeyDown(_settings.PeekVirtualKey));
            _peekDown = false;
        }
        UpdateCurrentRevealSettings();
        if (!StartupManager.Apply(_settings.StartWithWindows))
        {
            _logger.Log(LogLevel.Warning, "Could not update the Windows startup registration.");
        }
        UpdateTrayStatus();
        return true;
    }

    private void AdjustRevealDiameter(int direction)
    {
        var updated = _settings.AdjustRevealDiameter(direction);
        if (updated.RevealDiameterPx == _settings.RevealDiameterPx)
        {
            return;
        }

        if (!_settingsStore.Save(updated))
        {
            ShowError(UiText.Text(_settings.Language, "settingsSaveFailed"));
            return;
        }

        _settings = updated;
        UpdateCurrentRevealSettings();
    }

    private void UpdateCurrentRevealSettings()
    {
        _coordinator.UpdateRevealSettings(new RevealSettings
        {
            DiameterPx = _settings.RevealDiameterPx,
            SoftEdgeWidthPx = _settings.RevealSoftEdgeWidthPx,
            BlurLevel = _settings.RevealBlurLevel,
            Shape = _settings.RevealShape,
            Trigger = _settings.PeekTrigger
        });
    }

    private void UpdateTrayStatus()
    {
        var profile = _coordinator.CurrentProfile;
        var target = profile?.Original.ProcessName ?? "none";
        var text = string.Format(UiText.Text(_settings.Language, "status"), UiText.State(_settings.Language, _coordinator.State), target);
        _statusItem.Header = text;
        _pickItem.Header = UiText.Text(_settings.Language, "pick");
        _toggleItem.Header = UiText.Text(_settings.Language, "toggle");
        _restoreItem.Header = UiText.Text(_settings.Language, "restore");
        _restoreAllItem.Header = UiText.Text(_settings.Language, "restoreAll");
        _settingsItem.Header = UiText.Text(_settings.Language, "settings");
        var update = _updates.Snapshot;
        var showUpdate = update.Status == ApplicationUpdateStatus.Available && update.Release is not null;
        _updateItem.IsVisible = showUpdate;
        _updateItem.Header = showUpdate
            ? string.Format(UiText.Text(_settings.Language, "updateAvailableTray"), update.Release!.VersionText)
            : string.Empty;
        _exitItem.Header = UiText.Text(_settings.Language, "exit");
        _restoreItem.IsEnabled = profile is not null;
        _restoreAllItem.IsEnabled = profile is not null;
        _toggleItem.IsEnabled = profile is not null;
        _trayIcon.ToolTipText = $"GhostSlacking - {UiText.State(_settings.Language, _coordinator.State)}";
    }

    private void OnUpdateStateChanged(object? sender, EventArgs args)
    {
        if (_isExiting)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateTrayStatus();
        }
        else
        {
            Dispatcher.UIThread.Post(UpdateTrayStatus);
        }
    }

    private void ShowError(string message)
    {
        if (_isExiting)
        {
            return;
        }

        _notifications.Show(message, UserNotificationSeverity.Error);
    }

    private void ShowCoreError(UserErrorKind kind)
    {
        ShowError(UiText.Error(_settings.Language, kind));
    }

    private void ShowInfo(string message)
    {
        if (_isExiting)
        {
            return;
        }

        _notifications.Show(message, UserNotificationSeverity.Info);
    }

    internal void ReportUnhandledException(Exception exception)
    {
        _logger.Log(LogLevel.Error, "An unexpected Avalonia UI error occurred.", exception);
        ShowError(UiText.Text(_settings.Language, "unexpected"));
    }

    private void ExitApplication()
    {
        ExitApplication(restoreAll: false);
    }

    private void ExitApplication(bool restoreAll)
    {
        if (_isExiting)
        {
            return;
        }

        _forceRestoreOnExit = restoreAll;
        _isExiting = true;
        try
        {
            Dispose();
        }
        finally
        {
            _lifetime.Shutdown();
        }
    }

    private void OnCloseRequested() => Dispatcher.UIThread.Post(() => ExitApplication(restoreAll: true));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isExiting = true;
        _updateCancellation.Cancel();
        CancelPicking();
        RestoreReport? restoreReport = null;
        if (_settings.RestoreOnExit || _forceRestoreOnExit)
        {
            restoreReport = _coordinator.RestoreAll("application exit");
        }

        _recovery.ProfilesChanged -= OnRecoveryProfilesChanged;
        _mouseHook.Dispose();
        _keyboardHook.Dispose();
        _hotkeys.Dispose();
        _timer.Stop();
        _revealOverlay.Dispose();
        if (_settingsWindow is not null)
        {
            _settingsWindow.Closed -= OnSettingsClosed;
            _settingsWindow.Close();
            _settingsWindow = null;
        }

        _notifications.Dispose();
        _updates.Changed -= OnUpdateStateChanged;
        _updates.Dispose();
        _updateCancellation.Dispose();
        _trayIcon.Clicked -= OnTrayIconClicked;
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();

        // A failed restore is intentionally not acknowledged as clean. Closing
        // the pipe leaves the last manifest for the watchdog timeout path. On a
        // clean exit, hooks and tray UI are already stopped before acknowledgment.
        if (restoreReport is null || !restoreReport.HasFailures)
        {
            _watchdog?.CompleteShutdown();
        }

        _watchdog?.Dispose();
        _messageWindow.CloseRequested -= OnCloseRequested;
        _messageWindow.DisplayConfigurationChanged -= OnDisplayConfigurationChanged;
        _messageWindow.Dispose();
        _logger.Log(LogLevel.Info, "Application exited.");
        _logger.Dispose();
        GC.SuppressFinalize(this);
    }

    private IWatchdogClient? TryStartWatchdog()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "GhostSlacking.Watchdog.exe");
        var client = new NamedPipeWatchdogClient(executable, _logger);
        try
        {
            client.Start();
            return client;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            client.Dispose();
            _logger.Log(LogLevel.Error, "Watchdog could not be started; crash recovery is unavailable.", exception);
            return null;
        }
    }

    private void OnRecoveryProfilesChanged(object? sender, EventArgs args) => PublishRecoveryManifest();

    private void PublishRecoveryManifest()
    {
        if (_watchdog is null || !_watchdog.IsConnected)
        {
            return;
        }

        var manifest = RecoveryManifest.FromProfiles(
            _watchdog.SessionId,
            _recovery.Profiles,
            DateTimeOffset.UtcNow);
        try
        {
            _watchdog.PublishManifest(manifest);
        }
        catch (IOException exception)
        {
            _logger.Log(LogLevel.Error, "Watchdog recovery manifest could not be published.", exception);
        }
    }

}
