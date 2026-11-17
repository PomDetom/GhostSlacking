using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Diagnostics;
using System.Drawing;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App;

internal sealed partial class GhostApplicationController : IDisposable
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
        _updates = new ApplicationUpdateManager(_logger, channel: _settings.UpdateChannel);
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
        _coordinator.EnvironmentChanging += (_, _) =>
        {
            _backgroundInteraction.Stop(BackgroundInteractionStopReason.TargetChanged);
            if (_settings.ExperimentalBackgroundInteraction) ResetPeekIntent();
        };
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

    private async void ExitApplication(bool restoreAll)
    {
        if (_isExiting)
        {
            return;
        }

        _forceRestoreOnExit = restoreAll;
        _isExiting = true;
        try
        {
            await _updates.PrepareForExitAsync();
        }
        finally
        {
            try { Dispose(); }
            finally { _lifetime.Shutdown(); }
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
