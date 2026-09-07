using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App;

internal sealed class GhostApplicationContext : ApplicationContext
{
    private const int WindowVisibilityHotkey = 1;
    private const int RestoreHotkey = 2;
    private const int EmergencyRestoreHotkey = 3;
    private const int SettingsHotkey = 4;
    private const int QuitHotkey = 5;
    private const int PickHotkey = 6;

    private readonly FileLogger _logger;
    private readonly SettingsStore _settingsStore;
    private readonly Win32WindowApi _windows;
    private readonly RecoveryManager _recovery;
    private readonly GhostCoordinator _coordinator;
    private readonly IWatchdogClient? _watchdog;
    private readonly Win32WindowPicker _picker;
    private readonly MessageWindow _messageWindow;
    private readonly Win32HotkeyManager _hotkeys;
    private readonly LowLevelMouseHook _mouseHook;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _pickItem;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly ToolStripMenuItem _restoreItem;
    private readonly ToolStripMenuItem _restoreAllItem;
    private AppSettings _settings;
    private bool _pickerActive;
    private bool _peekDown;
    private bool _isExiting;
    private readonly PeekStateTracker _peekState = new();
    private SettingsForm? _settingsForm;

    public GhostApplicationContext()
    {
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        _logger = new FileLogger(_settings);
        _windows = new Win32WindowApi();
        var backend = new Win32VisibilityBackend(_logger);
        _recovery = new RecoveryManager(_windows, backend, _logger);
        var visibility = new VisibilityEngine(backend, _logger);
        _coordinator = new GhostCoordinator(_windows, _recovery, visibility, _logger);
        _messageWindow = new MessageWindow();
        _messageWindow.HotkeyPressed += OnHotkeyPressed;
        _hotkeys = new Win32HotkeyManager(_messageWindow.Handle);
        _mouseHook = new LowLevelMouseHook();
        _mouseHook.LeftButtonDown += OnPickerClick;
        _picker = new Win32WindowPicker((uint)Environment.ProcessId, () => [_messageWindow.Handle]);

        _statusItem = new ToolStripMenuItem(UiText.Text(_settings.Language, "status")) { Enabled = false };
        _restoreItem = new ToolStripMenuItem(UiText.Text(_settings.Language, "restore"), null, (_, _) => RestoreCurrent("tray"));
        _restoreAllItem = new ToolStripMenuItem(UiText.Text(_settings.Language, "restoreAll"), null, (_, _) => RestoreAll("tray"));
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        _pickItem = new ToolStripMenuItem(UiText.Text(_settings.Language, "pick"), null, (_, _) => BeginPicking());
        _toggleItem = new ToolStripMenuItem(UiText.Text(_settings.Language, "toggle"), null, (_, _) => ToggleWindowVisibility());
        _settingsItem = new ToolStripMenuItem(UiText.Text(_settings.Language, "settings"), null, (_, _) => OpenSettings());
        _exitItem = new ToolStripMenuItem(UiText.Text(_settings.Language, "exit"), null, (_, _) => ExitApplication());
        menu.Items.Add(_pickItem);
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_restoreItem);
        menu.Items.Add(_restoreAllItem);
        menu.Items.Add(_settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIcon.Instance,
            Text = $"GhostSlacking - {UiText.State(_settings.Language, GhostState.Idle)}",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => BeginPicking();

        _coordinator.StateChanged += (_, _) => UpdateTrayStatus();
        _coordinator.UserError += (_, message) => ShowError(message);
        _coordinator.TargetClosed += (_, _) => UpdateTrayStatus();
        _watchdog = TryStartWatchdog();
        _recovery.ProfilesChanged += OnRecoveryProfilesChanged;
        PublishRecoveryManifest();

        _timer = new System.Windows.Forms.Timer { Interval = 33 };
        _timer.Tick += (_, _) =>
        {
            _peekDown = _peekState.Update(_windows.IsKeyDown(_settings.PeekVirtualKey), _settings.PeekTrigger);
            _coordinator.UpdatePeek(_windows.GetCursorPosition(), _peekDown);
        };
        _timer.Start();

        RegisterHotkeys();
        Application.ApplicationExit += OnApplicationExit;
        UpdateTrayStatus();
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
            (QuitHotkey, _settings.ExitHotkey)
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
        }
    }

    private void BeginPicking()
    {
        if (_pickerActive)
        {
            return;
        }

        _coordinator.BeginPicking();
        _pickerActive = _mouseHook.Start();
        if (!_pickerActive)
        {
            _coordinator.CancelPicking();
            ShowError("Could not start window picking.");
        }
        else
        {
            _notifyIcon.ShowBalloonTip(1500, "GhostSlacking", UiText.Text(_settings.Language, "clickToPick"), ToolTipIcon.Info);
        }
    }

    private void OnPickerClick(object? sender, MouseButtonEventArgs args)
    {
        _messageWindow.Post(() => FinishPicking(args.ScreenPoint));
    }

    private void FinishPicking(Point screenPoint)
    {
        if (!_pickerActive)
        {
            return;
        }

        _pickerActive = false;
        _mouseHook.Stop();
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
            Shape = _settings.RevealShape,
            Trigger = _settings.PeekTrigger
        };
        if (!_coordinator.SelectWindow(target, settings))
        {
            return;
        }

        _notifyIcon.ShowBalloonTip(1200, "GhostSlacking", string.Format(UiText.Text(_settings.Language, "windowSelected"), target.ProcessName ?? (UiText.IsChinese(_settings.Language) ? "窗口" : "window"), UiText.PeekKeyName(_settings.Language, _settings.PeekVirtualKey)), ToolTipIcon.Info);
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
        var report = _coordinator.RestoreAll(reason);
        if (report.HasFailures)
        {
            ShowError(UiText.Text(_settings.Language, "restoreSome"));
        }
        UpdateTrayStatus();
    }

    private void OpenSettings()
    {
        if (_settingsForm is not null && !_settingsForm.IsDisposed)
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized)
            {
                _settingsForm.WindowState = FormWindowState.Normal;
            }

            _settingsForm.Activate();
            _settingsForm.BringToFront();
            return;
        }

        _hotkeys.UnregisterAll();
        try
        {
            using var form = new SettingsForm(_settings);
            _settingsForm = form;
            form.SaveRequested += () => SaveSettings(form);
            form.ShowDialog();
        }
        finally
        {
            _settingsForm = null;
            if (!_isExiting)
            {
                RegisterHotkeys();
            }
        }
    }

    private bool SaveSettings(SettingsForm form)
    {
        var previousKey = _settings.PeekVirtualKey;
        var previousTrigger = _settings.PeekTrigger;
        _settings = form.GetSettings(_settings).Normalize();
        if (previousKey != _settings.PeekVirtualKey || previousTrigger != _settings.PeekTrigger)
        {
            _peekState.Reset(_windows.IsKeyDown(_settings.PeekVirtualKey));
            _peekDown = false;
        }
        var saved = _settingsStore.Save(_settings);
        if (!StartupManager.Apply(_settings.StartWithWindows))
        {
            _logger.Log(LogLevel.Warning, "Could not update the Windows startup registration.");
        }
        UpdateTrayStatus();
        return saved;
    }

    private void UpdateTrayStatus()
    {
        var profile = _coordinator.CurrentProfile;
        var target = profile?.Original.ProcessName ?? "none";
        var text = string.Format(UiText.Text(_settings.Language, "status"), UiText.State(_settings.Language, _coordinator.State), target);
        _statusItem.Text = text;
        _pickItem.Text = UiText.Text(_settings.Language, "pick");
        _toggleItem.Text = UiText.Text(_settings.Language, "toggle");
        _restoreItem.Text = UiText.Text(_settings.Language, "restore");
        _restoreAllItem.Text = UiText.Text(_settings.Language, "restoreAll");
        _settingsItem.Text = UiText.Text(_settings.Language, "settings");
        _exitItem.Text = UiText.Text(_settings.Language, "exit");
        _restoreItem.Enabled = profile is not null;
        _restoreAllItem.Enabled = profile is not null;
        _toggleItem.Enabled = profile is not null;
        _notifyIcon.Text = $"GhostSlacking - {UiText.State(_settings.Language, _coordinator.State)}";
    }

    private void ShowError(string message)
    {
        if (_isExiting)
        {
            return;
        }

        _notifyIcon.ShowBalloonTip(2500, "GhostSlacking", UiText.Error(_settings.Language, message), ToolTipIcon.Error);
    }

    private void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        ExitThread();
    }

    private void OnApplicationExit(object? sender, EventArgs args)
    {
        RestoreReport? restoreReport = null;
        if (_settings.RestoreOnExit)
        {
            restoreReport = _coordinator.RestoreAll("application exit");
        }

        // A failed restore is intentionally not acknowledged as clean. Closing
        // the pipe leaves the last manifest for the watchdog timeout path.
        if (restoreReport is null || !restoreReport.HasFailures)
        {
            _watchdog?.CompleteShutdown();
        }

        _recovery.ProfilesChanged -= OnRecoveryProfilesChanged;
        _watchdog?.Dispose();

        _mouseHook.Dispose();
        _hotkeys.Dispose();
        _timer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _messageWindow.Dispose();
        _logger.Log(LogLevel.Info, "Application exited.");
        _logger.Dispose();
        Application.ApplicationExit -= OnApplicationExit;
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

    private sealed class MessageWindow : NativeWindow, IDisposable
    {
        public MessageWindow()
        {
            _invoker.CreateControl();
            CreateHandle(new CreateParams { Caption = "GhostSlacking.MessageWindow" });
        }

        private readonly Control _invoker = new();
        public event Action<int>? HotkeyPressed;

        public void Post(Action action)
        {
            if (_invoker.IsHandleCreated)
            {
                _invoker.BeginInvoke(action);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                HotkeyPressed?.Invoke(m.WParam.ToInt32());
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            DestroyHandle();
            _invoker.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
