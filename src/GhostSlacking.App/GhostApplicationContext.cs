using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App;

internal sealed class GhostApplicationContext : ApplicationContext
{
    private const int PickOrToggleHotkey = 1;
    private const int RestoreHotkey = 2;
    private const int EmergencyRestoreHotkey = 3;
    private const int QuitHotkey = 4;

    private readonly FileLogger _logger;
    private readonly SettingsStore _settingsStore;
    private readonly Win32WindowApi _windows;
    private readonly GhostCoordinator _coordinator;
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

    public GhostApplicationContext()
    {
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        _logger = new FileLogger(_settings);
        _windows = new Win32WindowApi();
        var backend = new Win32VisibilityBackend(_logger);
        var recovery = new RecoveryManager(_windows, backend, _logger);
        var visibility = new VisibilityEngine(backend, _logger);
        _coordinator = new GhostCoordinator(_windows, recovery, visibility, _logger);
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
        _toggleItem = new ToolStripMenuItem(UiText.Text(_settings.Language, "toggle"), null, (_, _) => ToggleGhostOrPick());
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
            Icon = SystemIcons.Application,
            Text = $"GhostSlacking - {UiText.State(_settings.Language, GhostState.Idle)}",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => BeginPicking();

        _coordinator.StateChanged += (_, _) => UpdateTrayStatus();
        _coordinator.UserError += (_, message) => ShowError(message);
        _coordinator.TargetClosed += (_, _) => UpdateTrayStatus();

        _timer = new System.Windows.Forms.Timer { Interval = 33 };
        _timer.Tick += (_, _) =>
        {
            _peekDown = _windows.IsKeyDown(_settings.PeekVirtualKey);
            _coordinator.UpdatePeek(_windows.GetCursorPosition(), _peekDown);
        };
        _timer.Start();

        RegisterHotkeys();
        Application.ApplicationExit += OnApplicationExit;
        UpdateTrayStatus();
    }

    private void RegisterHotkeys()
    {
        Register(PickOrToggleHotkey, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat, 0x47, "Ctrl+Alt+G");
        Register(RestoreHotkey, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat, 0x52, "Ctrl+Alt+R");
        Register(EmergencyRestoreHotkey, HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat, 0x52, "Ctrl+Shift+Alt+R");
        Register(QuitHotkey, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat, 0x51, "Ctrl+Alt+Q");
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
            case PickOrToggleHotkey:
                ToggleGhostOrPick();
                break;
            case RestoreHotkey:
                RestoreCurrent("hotkey");
                break;
            case EmergencyRestoreHotkey:
                RestoreAll("emergency hotkey");
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
            Shape = _settings.RevealShape
        };
        if (!_coordinator.SelectWindow(target, settings))
        {
            return;
        }

        _notifyIcon.ShowBalloonTip(1200, "GhostSlacking", string.Format(UiText.Text(_settings.Language, "windowSelected"), target.ProcessName ?? (UiText.IsChinese(_settings.Language) ? "窗口" : "window"), UiText.PeekKeyName(_settings.Language, _settings.PeekVirtualKey)), ToolTipIcon.Info);
    }

    private void ToggleGhostOrPick()
    {
        if (_coordinator.CurrentProfile is null)
        {
            BeginPicking();
        }
        else
        {
            _coordinator.ToggleGhost("toggle hotkey");
        }
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
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        _settings = form.GetSettings(_settings).Normalize();
        _settingsStore.Save(_settings);
        if (!StartupManager.Apply(_settings.StartWithWindows))
        {
            _logger.Log(LogLevel.Warning, "Could not update the Windows startup registration.");
        }
        UpdateTrayStatus();
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
        if (_settings.RestoreOnExit)
        {
            _coordinator.RestoreAll("application exit");
        }

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
