using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
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
    internal const int DiameterIncreaseHotkey = 7;
    internal const int DiameterDecreaseHotkey = 8;
    internal const int BackgroundInteractionHotkey = 9;
    private readonly Win32BackgroundInteraction _backgroundInteraction;
    private readonly AvaloniaNotificationService _backgroundHud;
    private bool _lastPeekKeyDown;
    private BackgroundArmStatus? _backgroundStartWait;
    private WindowSnapshot? _backgroundWaitTarget;
    private bool _invalidContinuousGesture;
    private readonly FileLogger _logger;
    private readonly DataExportService _dataExport;
    private readonly UpdateCompletionStore _updateCompletionStore;
    private readonly IApplicationUpdateManager _updates;
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly IUserNotificationService _notifications;
    private readonly SettingsStore _settingsStore;
    private readonly Win32WindowApi _windows;
    private readonly RecoveryManager _recovery;
    private readonly GhostCoordinator _coordinator;
    private readonly IWatchdogClient? _watchdog;
    private readonly InstallationControlServer _installationControl;
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
    private UpdateWindow? _updateWindow;
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
        _updateCompletionStore = new UpdateCompletionStore(logger: _logger);
        _updates = new GitHubApplicationUpdateManager(_logger, channel: _settings.UpdateChannel);
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
        _updateItem = CreateMenuItem(string.Empty, OpenUpdateWindow);
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
        _notifications = new AvaloniaNotificationService(
            _windows.GetCursorPosition,
            _logger,
            _windows.BringToTopWithoutActivation,
            _settings,
            _windows.SetNotificationClickThrough);
        _backgroundHud = new AvaloniaNotificationService(
            _windows.GetCursorPosition, _logger, _windows.BringToTopWithoutActivation,
            _settings, _windows.SetNotificationClickThrough);
        _backgroundInteraction = new Win32BackgroundInteraction(
            _windows, () => _coordinator.State, () => _coordinator.CurrentProfile,
            action => Dispatcher.UIThread.Post(action), _logger);
        _backgroundInteraction.Preparing += waiting =>
            _backgroundHud.ShowPersistent(UiText.Text(_settings.Language, waiting ? "backgroundPending" : "backgroundStarting"));
        _backgroundInteraction.Started += () =>
        {
            _backgroundHud.ShowPersistent(UiText.Text(_settings.Language, "backgroundActive"));
            UpdateTrayStatus();
        };
        _backgroundInteraction.InputPending += pending =>
        {
            if (!_isExiting) _backgroundHud.ShowPersistent(UiText.Text(_settings.Language, pending ? "backgroundStarting" : "backgroundActive"));
        };
        _backgroundInteraction.Stopped += OnBackgroundInteractionStopped;
        _backgroundInteraction.InputRejected += OnBackgroundInputRejected;
        _backgroundInteraction.InputResumed += () =>
        {
            if (!_isExiting) _backgroundHud.ShowPersistent(UiText.Text(_settings.Language, "backgroundActive"));
        };
        _backgroundInteraction.ShortcutPressed += id =>
        {
            // The hook stops the session for its own toggle, which must not arm it again.
            if (id != BackgroundInteractionHotkey) OnHotkeyPressed(id);
        };
        _updates.Changed += OnUpdateStateChanged;
        _updates.InstallHandoffStarted += OnUpdateInstallHandoffStarted;

        _coordinator.StateChanged += (_, change) =>
        {
            if (change.State != GhostState.Reveal)
            {
                _backgroundInteraction.Stop(change.State is GhostState.Ghost or GhostState.Restoring
                    ? BackgroundInteractionStopReason.RevealEnded : BackgroundInteractionStopReason.TargetChanged);
                if (change.State != GhostState.Ghost) ResetPeekIntent();
                if (change.State is GhostState.Visible or GhostState.Idle) _backgroundInteraction.RecordTargetInputState("restored");
            }
            UpdateTrayStatus();
        };
        _coordinator.UserErrorOccurred += (_, error) =>
        {
            _backgroundInteraction.Stop(BackgroundInteractionStopReason.DeliveryFailed);
            ShowCoreError(error.Kind);
        };
        _coordinator.WindowSelected += OnWindowSelected;
        _coordinator.TargetClosed += (_, _) => UpdateTrayStatus();
        _watchdog = TryStartWatchdog();
        _installationControl = new InstallationControlServer(AppContext.BaseDirectory, OnCloseRequested, _logger);
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
        var updateResult = _updateCompletionStore.Consume();
        if (updateResult is null)
        {
            ShowInfo(UiText.Text(_settings.Language, "startupReady"), UserNotificationSource.Startup);
            return;
        }

        _logger.Log(
            updateResult.Status == UpdateCompletionStatus.Failed ? LogLevel.Warning : LogLevel.Info,
            $"UpdateCompletion status={updateResult.Status} version={updateResult.Version} exitCode={updateResult.InstallerExitCode}");
        switch (updateResult.Status)
        {
            case UpdateCompletionStatus.LaunchFailed:
                ShowError(UiText.Text(_settings.Language, "updateLaunchFailed"), UserNotificationSource.Error);
                break;
            case UpdateCompletionStatus.Succeeded:
                ShowSuccess(UiText.Text(_settings.Language, "updateCompleted"), UserNotificationSource.Startup);
                break;
            case UpdateCompletionStatus.Cancelled:
                ShowInfo(UiText.Text(_settings.Language, "updateCancelled"), UserNotificationSource.Startup);
                break;
            default:
                ShowError(UiText.Text(_settings.Language, "automaticUpdateFailed"), UserNotificationSource.Error);
                break;
        }
    }

    internal async void BeginAutomaticUpdateCheck()
    {
        try
        {
            var snapshot = await _updates.CheckAsync(_updateCancellation.Token);
            if (snapshot.Status == ApplicationUpdateStatus.Available && snapshot.Release is not null && !_isExiting)
            {
                await Dispatcher.UIThread.InvokeAsync(() => _notifications.Show(
                    UiText.Text(_settings.Language, "updateAvailableNotification"),
                    UserNotificationSeverity.Info,
                    OpenUpdateWindow,
                    UserNotificationSource.Update));
            }
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested)
        {
        }
    }

    private void OnPeekTimerTick(object? sender, EventArgs args)
    {
        var peekKeyDown = _windows.IsKeyDown(_settings.PeekVirtualKey);
        var peekPressed = peekKeyDown && !_lastPeekKeyDown;
        _lastPeekKeyDown = peekKeyDown;
        if (!peekKeyDown) _invalidContinuousGesture = false;
        _backgroundInteraction.Poll();
        if (_backgroundStartWait is not null && !BackgroundStartPolicy.KeepWaiting(_backgroundWaitTarget, _coordinator.CurrentProfile?.Original, _peekDown)) ResetPeekIntent();
        if (_backgroundInteraction.BlocksReveal && peekPressed)
        {
            _backgroundInteraction.AcknowledgeRevealBlock();
        }
        if (_backgroundInteraction.BlocksReveal)
        {
            _peekState.Reset(peekKeyDown);
            _peekDown = false;
        }
        else
        {
            _peekDown = _peekState.Update(peekKeyDown, _settings.PeekTrigger);
        }
        var cursor = _windows.GetCursorPosition();
        var startedAt = Stopwatch.GetTimestamp();
        var continuous = _settings.ExperimentalBackgroundInteraction && _settings.ContinuousBackgroundInteraction;
        var show = _peekDown;
        if (continuous)
        {
            if (_settings.PeekTrigger != PeekTrigger.Toggle)
            {
                if (_peekDown && !_invalidContinuousGesture)
                {
                    _invalidContinuousGesture = true;
                    ShowError(UiText.Text(_settings.Language, "backgroundToggleRequired"));
                }
                ResetPeekIntent();
                show = false;
            }
            else if (_isExiting || _pickerActive || _settingsWindow is not null || _updateWindow is not null)
            {
                _backgroundInteraction.Stop(BackgroundInteractionStopReason.RevealEnded);
                ResetPeekIntent();
                show = false;
            }
            else if (!_peekDown || _coordinator.CurrentProfile?.Original.ScreenBounds.Contains(cursor) != true)
            {
                _backgroundInteraction.Stop(_peekDown ? BackgroundInteractionStopReason.CursorLeft : BackgroundInteractionStopReason.RevealEnded);
                if (!_peekDown) ClearBackgroundStartWait();
                show = false;
            }
            else
            {
                if (!_backgroundInteraction.IsActive && !_backgroundInteraction.IsPending && _coordinator.State == GhostState.Reveal)
                    _coordinator.UpdatePeek(cursor, false);
                if (_backgroundInteraction.IsDraining && !_backgroundInteraction.BlocksReveal) SetBackgroundStartWait(BackgroundArmStatus.AwaitingRelease);
                if (!_backgroundInteraction.IsActive && !_backgroundInteraction.IsPending &&
                    !_backgroundInteraction.IsDraining && !_backgroundInteraction.BlocksReveal &&
                    BackgroundStartPolicy.CanResume(_backgroundStartWait, _backgroundInteraction.HasSeparateForeground) &&
                    _coordinator.State is GhostState.Ghost or GhostState.Reveal)
                {
                    var result = ArmBackgroundInteraction(prepareHidden: true);
                    if (result.Status is BackgroundArmStatus.AwaitingForeground or BackgroundArmStatus.AwaitingRelease)
                        SetBackgroundStartWait(result.Status);
                    else if (result.Status == BackgroundArmStatus.Preparing)
                        ClearBackgroundStartWait(hide: false);
                    else
                    {
                        ClearBackgroundStartWait();
                        _logger.Log(LogLevel.Warning, $"ContinuousBackgroundInteraction prepare failed operation={result.Operation} error={result.ErrorCode}");
                        ResetPeekIntent();
                        ShowError(UiText.Text(_settings.Language, "backgroundFailed"));
                    }
                }
                _backgroundInteraction.Poll();
                show = _peekDown && (_backgroundInteraction.IsReady || _backgroundInteraction.IsActive);
            }
        }
        _coordinator.UpdatePeek(cursor, show);
        if (continuous && show && _backgroundInteraction.IsReady && !_backgroundInteraction.ActivateReveal())
        {
            // A new event arrived while the target was being shown. Keep it gated,
            // hide again and prepare a fresh session after the physical release.
            _backgroundInteraction.Stop(BackgroundInteractionStopReason.RevealEnded);
            _coordinator.UpdatePeek(cursor, false);
        }
        _backgroundInteraction.Poll();
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
        _notifications?.RefreshLayout();
        _backgroundHud?.RefreshLayout();
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
        var shortcuts = GetShortcuts();
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

    private (int Id, HotkeyBinding Binding)[] GetShortcuts() =>
        [
            (PickHotkey, _settings.PickHotkey),
            (WindowVisibilityHotkey, _settings.WindowToggleHotkey),
            (RestoreHotkey, _settings.RestoreHotkey),
            (EmergencyRestoreHotkey, _settings.RestoreAllHotkey),
            (SettingsHotkey, _settings.SettingsHotkey),
            (QuitHotkey, _settings.ExitHotkey),
            (DiameterIncreaseHotkey, _settings.RevealDiameterIncreaseHotkey),
            (DiameterDecreaseHotkey, _settings.RevealDiameterDecreaseHotkey),
            (BackgroundInteractionHotkey, _settings.ExperimentalBackgroundInteraction
                ? _settings.BackgroundInteractionHotkey : HotkeyBinding.Disabled)
        ];

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
        DispatchHotkey(id, CancelBackgroundInteraction, ExecuteHotkey);
    }

    internal static void DispatchHotkey(int id, Action cancel, Action<int> execute)
    {
        if (id != BackgroundInteractionHotkey && !PreservesReveal(id)) cancel();
        execute(id);
    }

    private void ExecuteHotkey(int id)
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
            case BackgroundInteractionHotkey:
                ToggleBackgroundInteraction();
                break;
        }
    }

    internal static bool PreservesReveal(int id) => id is DiameterIncreaseHotkey or DiameterDecreaseHotkey;

    private void ToggleBackgroundInteraction()
    {
        if (_backgroundStartWait is not null)
        {
            CancelBackgroundInteraction();
            return;
        }
        if (_backgroundInteraction.IsActive || _backgroundInteraction.IsPending)
        {
            _backgroundInteraction.Stop();
            return;
        }
        if (!_settings.ExperimentalBackgroundInteraction) return;
        if (_settings.ContinuousBackgroundInteraction)
        {
            var hadIntent = _peekDown;
            CancelBackgroundInteraction();
            ShowInfo(UiText.Text(_settings.Language, hadIntent ? "backgroundStopped" : "backgroundUnavailable"));
            return;
        }
        if (_settings.PeekTrigger != PeekTrigger.Toggle)
        {
            ShowError(UiText.Text(_settings.Language, "backgroundToggleRequired"));
            return;
        }
        var result = ArmBackgroundInteraction();
        if (result.Status is BackgroundArmStatus.AwaitingForeground or BackgroundArmStatus.AwaitingRelease)
        {
            CancelBackgroundInteraction();
            ShowInfo(UiText.Text(_settings.Language, result.Status == BackgroundArmStatus.AwaitingForeground ? "backgroundFocusFirst" : "backgroundPending"));
        }
        else if (!result.Success)
        {
            _logger.Log(LogLevel.Warning, $"BackgroundInteraction arm failed operation={result.Operation} error={result.ErrorCode}");
            ShowError(UiText.Text(_settings.Language, _coordinator.State == GhostState.Reveal ? "backgroundFailed" : "backgroundUnavailable"));
        }
    }

    private BackgroundArmResult ArmBackgroundInteraction(bool prepareHidden = false)
    {
        var shortcuts = GetShortcuts().Where(item => !item.Binding.IsDisabled)
            .GroupBy(item => item.Binding).Where(group => group.Count() == 1).SelectMany(group => group)
            .ToDictionary(item => item.Id, item => item.Binding);
        var preservingShortcuts = shortcuts.Keys.Where(PreservesReveal).ToHashSet();
        return _backgroundInteraction.Arm(shortcuts, preservingShortcuts, prepareHidden, _settings.PeekVirtualKey);
    }

    private void SetBackgroundStartWait(BackgroundArmStatus status)
    {
        if (_backgroundStartWait == status && _backgroundWaitTarget == _coordinator.CurrentProfile?.Original) return;
        _backgroundStartWait = status;
        _backgroundWaitTarget = _coordinator.CurrentProfile?.Original;
        _backgroundHud.ShowPersistent(UiText.Text(_settings.Language, status == BackgroundArmStatus.AwaitingForeground ? "backgroundFocusFirst" : "backgroundPending"));
    }

    private void ClearBackgroundStartWait(bool hide = true)
    {
        if (_backgroundStartWait is null) return;
        _backgroundStartWait = null;
        _backgroundWaitTarget = null;
        if (hide) _backgroundHud.Hide();
    }

    private void ResetPeekIntent()
    {
        ClearBackgroundStartWait();
        _peekState.Reset(_windows.IsKeyDown(_settings.PeekVirtualKey));
        _peekDown = false;
    }

    private void CancelBackgroundInteraction()
    {
        ResetPeekIntent();
        _backgroundInteraction.Stop(BackgroundInteractionStopReason.RevealEnded);
        if (_coordinator.State == GhostState.Reveal)
            _coordinator.UpdatePeek(_windows.GetCursorPosition(), false);
        _backgroundHud.Hide();
    }

    private void OnBackgroundInteractionStopped(BackgroundInteractionStopReason reason)
    {
        if (_isExiting) return;
        if (_backgroundStartWait is not null && !_backgroundInteraction.BlocksReveal &&
            reason is BackgroundInteractionStopReason.CursorLeft or BackgroundInteractionStopReason.RevealEnded)
        {
            UpdateTrayStatus();
            return;
        }
        _backgroundHud.Hide();
        if (_backgroundInteraction.BlocksReveal)
        {
            _peekState.Reset(_windows.IsKeyDown(_settings.PeekVirtualKey));
            _peekDown = false;
            if (_coordinator.State == GhostState.Reveal)
                _coordinator.UpdatePeek(_windows.GetCursorPosition(), false);
        }
        if (reason == BackgroundInteractionStopReason.PeekClosed ||
            (_settings.ExperimentalBackgroundInteraction && _settings.ContinuousBackgroundInteraction &&
             reason is BackgroundInteractionStopReason.RevealEnded or BackgroundInteractionStopReason.CursorLeft))
        {
            UpdateTrayStatus();
            return;
        }
        var key = reason switch
        {
            BackgroundInteractionStopReason.ForegroundChanged => "backgroundForegroundChanged",
            BackgroundInteractionStopReason.UnsupportedInput or BackgroundInteractionStopReason.TargetChanged => "backgroundUnsupported",
            BackgroundInteractionStopReason.DeliveryFailed => "backgroundFailed",
            BackgroundInteractionStopReason.CleanupFailed => "backgroundCleanupFailed",
            _ => "backgroundStopped"
        };
        if (reason is BackgroundInteractionStopReason.UnsupportedInput or BackgroundInteractionStopReason.DeliveryFailed or BackgroundInteractionStopReason.CleanupFailed)
            ShowError(UiText.Text(_settings.Language, key));
        else ShowInfo(UiText.Text(_settings.Language, key));
        UpdateTrayStatus();
    }

    private void OnBackgroundInputRejected(BackgroundInputRejectionReason reason)
    {
        if (_isExiting || !_backgroundInteraction.IsActive) return;
        var key = reason switch
        {
            BackgroundInputRejectionReason.PendingTextOperation => "backgroundStarting",
            BackgroundInputRejectionReason.DiscoveryUnavailable => "backgroundDiscoveryRejected",
            BackgroundInputRejectionReason.PasteUnavailable => "backgroundPasteRejected",
            BackgroundInputRejectionReason.PasteRequiresEmptyEditor => "backgroundEmptyEditorRequired",
            BackgroundInputRejectionReason.RichContentUnverified => "backgroundRichContentRejected",
            BackgroundInputRejectionReason.ClipboardUnavailable => "backgroundClipboardRejected",
            BackgroundInputRejectionReason.ControlUnavailable => "backgroundControlRejected",
            BackgroundInputRejectionReason.NoInputRecipient => "backgroundClickEditor",
            BackgroundInputRejectionReason.ImeComposition => "backgroundImeRejected",
            BackgroundInputRejectionReason.DeadKey => "backgroundDeadKeyRejected",
            BackgroundInputRejectionReason.SystemShortcut => "backgroundSystemKeyRejected",
            BackgroundInputRejectionReason.UnsupportedMouse => "backgroundMouseRejected",
            BackgroundInputRejectionReason.OutsideEditor => "backgroundTextOnly",
            _ => "backgroundKeyRejected"
        };
        _backgroundHud.ShowPersistent(UiText.Text(_settings.Language, key));
    }

    private void BeginPicking()
    {
        CancelBackgroundInteraction();
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
            ShowInfo(UiText.Text(_settings.Language, "clickToPick"), UserNotificationSource.Picker);
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
                ShowInfo(UiText.Text(_settings.Language, "pickCancelled"), UserNotificationSource.Picker);
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

    }

    private void ToggleWindowVisibility()
    {
        CancelBackgroundInteraction();
        if (_coordinator.CurrentProfile is null)
        {
            ShowError(UiText.Text(_settings.Language, "selectWindowFirst"));
            return;
        }

        _coordinator.ToggleWindowVisibility("window visibility hotkey");
    }

    private void RestoreCurrent(string reason)
    {
        CancelBackgroundInteraction();
        _coordinator.RestoreCurrent(reason);
        UpdateTrayStatus();
    }

    private void RestoreAll(string reason)
    {
        CancelBackgroundInteraction();
        _coordinator.RestoreAll(reason);
        UpdateTrayStatus();
    }

    private void OpenSettings(string? initialPage = null)
    {
        CancelBackgroundInteraction();
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
                openUpdateWindow: OpenUpdateWindow,
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

    private void OpenUpdateWindow()
    {
        CancelBackgroundInteraction();
        if (_isExiting || _updates.Snapshot.Release is null)
        {
            return;
        }

        if (_updateWindow is not null)
        {
            _updateWindow.RefreshLanguage(_settings.Language);
            ActivateOwnedWindow(_updateWindow);
            return;
        }

        try
        {
            var window = new UpdateWindow(_updates, _settings.Language);
            _updateWindow = window;
            window.Closed += OnUpdateWindowClosed;
            window.Show();
            window.Activate();
        }
        catch (Exception exception)
        {
            _updateWindow = null;
            _logger.Log(LogLevel.Error, "The update window could not be shown.", exception);
            ShowError(UiText.Text(_settings.Language, "updateWindowOpenFailed"));
        }
    }

    private void OnUpdateWindowClosed(object? sender, EventArgs args)
    {
        if (_updateWindow is null)
        {
            return;
        }

        _updateWindow.Closed -= OnUpdateWindowClosed;
        _updateWindow = null;
    }

    private void ActivateOwnedWindow(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        Dispatcher.UIThread.Post(() =>
        {
            if (_isExiting || !window.IsVisible)
            {
                return;
            }

            var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
            var result = _windows.BringToForeground(handle);
            if (result.Success)
            {
                return;
            }

            _logger.Log(
                LogLevel.Warning,
                $"Update window foreground activation failed error={result.ErrorCode} message={result.ErrorMessage}");
            window.Activate();
        }, DispatcherPriority.Background);
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
        CancelBackgroundInteraction();
        var previousKey = _settings.PeekVirtualKey;
        var previousTrigger = _settings.PeekTrigger;
        updated = updated.Normalize();
        if (!_settingsStore.Save(updated))
        {
            return false;
        }

        _settings = updated;
        _notifications.Configure(_settings);
        _backgroundHud.Configure(_settings);
        _updates.SetChannel(_settings.UpdateChannel);
        _logger.MinimumLevel = _settings.MinimumLogLevel;
        AppTheme.Apply(_settings.ThemeMode);
        _updateWindow?.RefreshLanguage(_settings.Language);
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
        var result = TryAdjustRevealDiameter(_settings, direction, _settingsStore.Save, out var updated);
        if (result == DiameterAdjustmentResult.SaveFailed)
        {
            ShowError(UiText.Text(_settings.Language, "settingsSaveFailed"));
            return;
        }
        if (result == DiameterAdjustmentResult.Unchanged) return;

        _settings = updated;
        UpdateCurrentRevealSettings();
    }

    internal static DiameterAdjustmentResult TryAdjustRevealDiameter(
        AppSettings current, int direction, Func<AppSettings, bool> save, out AppSettings updated)
    {
        var candidate = current.AdjustRevealDiameter(direction);
        updated = current;
        if (candidate.RevealDiameterPx == current.RevealDiameterPx) return DiameterAdjustmentResult.Unchanged;
        if (!save(candidate)) return DiameterAdjustmentResult.SaveFailed;
        updated = candidate;
        return DiameterAdjustmentResult.Updated;
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
        var showUpdate = update.Release is not null &&
            update.Release.Version > ReleaseVersion.Parse(update.CurrentVersion) &&
            update.Status is not ApplicationUpdateStatus.Skipped and not ApplicationUpdateStatus.UpToDate and
                not ApplicationUpdateStatus.NoCompatibleRelease;
        _updateItem.IsVisible = showUpdate;
        _updateItem.Header = showUpdate
            ? string.Format(UiText.Text(_settings.Language, "updateAvailableTray"), update.Release!.VersionText)
            : string.Empty;
        _exitItem.Header = UiText.Text(_settings.Language, "exit");
        _restoreItem.IsEnabled = profile is not null;
        _restoreAllItem.IsEnabled = profile is not null;
        _toggleItem.IsEnabled = profile is not null;
        _trayIcon.ToolTipText = $"GhostSlacking - {UiText.State(_settings.Language, _coordinator.State)}";
        if (_backgroundInteraction.IsActive)
        {
            _trayIcon.ToolTipText += $" / {UiText.Text(_settings.Language, "backgroundActive")}";
        }
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

    private void ShowError(string message, UserNotificationSource source = UserNotificationSource.Error)
    {
        if (_isExiting)
        {
            return;
        }

        _notifications.Show(message, UserNotificationSeverity.Error, source: source);
    }

    private void ShowCoreError(UserErrorKind kind)
    {
        ShowError(UiText.Error(_settings.Language, kind));
    }

    private void ShowInfo(
        string message,
        UserNotificationSource source = UserNotificationSource.General)
    {
        if (_isExiting)
        {
            return;
        }

        _notifications.Show(message, UserNotificationSeverity.Info, source: source);
    }

    private void ShowSuccess(string message, UserNotificationSource source)
    {
        if (_isExiting)
        {
            return;
        }

        _notifications.Show(message, UserNotificationSeverity.Success, source: source);
    }

    private void OnWindowSelected(TargetWindow target) =>
        ShowSuccess(UiText.Text(_settings.Language, "windowSelected"), UserNotificationSource.Selection);

    internal void ReportUnhandledException(Exception exception)
    {
        _backgroundInteraction.Stop(BackgroundInteractionStopReason.DeliveryFailed);
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

    private void OnUpdateInstallHandoffStarted(object? sender, EventArgs args) =>
        Dispatcher.UIThread.Post(() => ShowInfo(UiText.Text(_settings.Language, "updatePreparing")));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isExiting = true;
        _updateCancellation.Cancel();
        _backgroundInteraction.Dispose();
        _backgroundHud.Dispose();
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

        if (_updateWindow is not null)
        {
            _updateWindow.Closed -= OnUpdateWindowClosed;
            _updateWindow.Close();
            _updateWindow = null;
        }

        _notifications.Dispose();
        _updates.Changed -= OnUpdateStateChanged;
        _updates.InstallHandoffStarted -= OnUpdateInstallHandoffStarted;
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
        _installationControl.CompleteShutdown(restoreReport is not null && !restoreReport.HasFailures &&
            (_watchdog is null || _watchdog is NamedPipeWatchdogClient { ShutdownSucceeded: true }));
        _installationControl.Dispose();
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
        return TryStartWatchdog(new NamedPipeWatchdogClient(executable, _logger), _logger);
    }

    internal static IWatchdogClient? TryStartWatchdog(IWatchdogClient client, ILogger logger)
    {
        try
        {
            client.Start();
            return client;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            client.Dispose();
            logger.Log(LogLevel.Error, "Watchdog could not be started; crash recovery is unavailable.", exception);
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
