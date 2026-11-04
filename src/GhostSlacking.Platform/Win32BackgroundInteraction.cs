using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

// Hooks only run during an explicitly armed session and its pending release tail.
// No callback injects foreground input or activates the target.
public enum BackgroundInteractionPhase { Idle, Preparing, Ready, Active, Draining }

public sealed class Win32BackgroundInteraction : IDisposable
{
    private readonly Win32WindowApi _windows;
    private readonly Func<GhostState> _state;
    private readonly Func<GhostWindowProfile?> _profile;
    private readonly Action<Action> _dispatch;
    private readonly ILogger _logger;
    private readonly Win32BackgroundInputBackend _backend;
    private readonly BackgroundInteractionSession _session;
    private readonly Win32NativeMethods.HookProc _keyboardCallback;
    private readonly Win32NativeMethods.HookProc _mouseCallback;
    private readonly Win32BackgroundInputNative.WinEventProc _foregroundCallback;
    private readonly HashSet<int> _physicalKeys = [];
    private IReadOnlyDictionary<int, HotkeyBinding> _shortcuts = new Dictionary<int, HotkeyBinding>();
    private GhostWindowProfile? _pending;
    private GhostWindowProfile? _armedProfile;
    private (BackgroundMouseButton Button, nint Recipient, Point Cursor, uint Time)? _lastClick;
    private nint _pendingForeground;
    private long _pendingStarted;
    private long _armAttemptStarted;
    private nint _keyboardHook;
    private nint _mouseHook;
    private nint _foregroundHook;
    private volatile bool _disposed;
    private volatile bool _inputBlocked;
    private volatile bool _hooksInstalled;
    private volatile bool _passThrough = true;
    private volatile bool _pendingHide;
    private QueuedMove? _queuedMove;
    private sealed class QueuedMove
    {
        public Win32NativeMethods.MSLLHOOKSTRUCT Packet;
        public Point Cursor;
        public BackgroundMouseMotion Motion;
        public bool Taken;
    }
    private static DateTimeOffset DrainClock => DateTimeOffset.UnixEpoch + Stopwatch.GetElapsedTime(0);
    private readonly BackgroundInputOwnership _ownership = new();
    private readonly BackgroundHookThread _hookThread;
    private readonly ConcurrentQueue<Action> _inputQueue = new();
    private bool _prepareHidden;
    private bool _sessionEstablished;
    private int _peekVirtualKey;
    private int _generation;
    private int _requestedStop = -1;
    private volatile BackgroundInteractionPhase _phase;
    private readonly HashSet<int> _borrowedKeys = [];
    private readonly HashSet<BackgroundMouseButton> _borrowedButtons = [];

    public Win32BackgroundInteraction(
        Win32WindowApi windows,
        Func<GhostState> state,
        Func<GhostWindowProfile?> profile,
        Action<Action> dispatch,
        ILogger? logger = null)
    {
        _windows = windows;
        _state = state;
        _profile = profile;
        _dispatch = dispatch;
        _logger = logger ?? NullLogger.Instance;
        _backend = new Win32BackgroundInputBackend(windows, _logger);
        _session = new BackgroundInteractionSession(windows, _backend);
        _session.Stopped += reason =>
        {
            NotifyStopped(reason);
        };
        _session.InputRejected += reason =>
        {
            _logger.Log(LogLevel.Info, $"BackgroundInputRejected reason={reason}");
            var generation = _generation;
            _dispatch(() => { if (generation == _generation && IsActive) InputRejected?.Invoke(reason); });
        };
        _session.PendingChanged += pending =>
        {
            if (IsActive && _session.IsActive && (pending || !_session.HasRejectedInput)) InputPending?.Invoke(pending);
        };
        _session.InputResumed += () =>
        {
            var generation = _generation;
            _dispatch(() => { if (generation == _generation && IsActive) InputResumed?.Invoke(); });
        };
        _keyboardCallback = KeyboardCallback;
        _mouseCallback = MouseCallback;
        _foregroundCallback = OnForegroundChanged;
        _hookThread = new BackgroundHookThread { Tick = DrainTick };
    }

    public BackgroundInteractionPhase Phase => _phase;
    public bool IsActive => _phase == BackgroundInteractionPhase.Active;
    public bool IsPending => _phase is BackgroundInteractionPhase.Preparing or BackgroundInteractionPhase.Ready;
    public bool IsReady => _phase == BackgroundInteractionPhase.Ready;
    public bool IsDraining => _phase == BackgroundInteractionPhase.Draining;
    private volatile bool _blocksReveal;
    public bool BlocksReveal { get => _blocksReveal; private set => _blocksReveal = value; }
    public event Action<bool>? Preparing;
    public event Action? Started;
    public event Action<BackgroundInteractionStopReason>? Stopped;
    public event Action<int>? ShortcutPressed;
    public event Action<BackgroundInputRejectionReason>? InputRejected;
    public event Action? InputResumed;
    public event Action<bool>? InputPending;
    public bool HasSeparateForeground => _profile() is { } profile && ForegroundStatus(_backend.ForegroundWindow, profile.Hwnd) == BackgroundArmStatus.Preparing;

    public BackgroundArmResult Arm(IReadOnlyDictionary<int, HotkeyBinding> shortcuts, bool prepareHidden = false, int peekVirtualKey = 0)
    {
        _armAttemptStarted = Stopwatch.GetTimestamp();
        var foreground = _backend.ForegroundWindow;
        var profile = _profile();
        BackgroundArmResult Refuse(BackgroundArmRejection reason) => LogStart(BackgroundArmResult.Reject(reason), foreground, profile);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393)) return Refuse(BackgroundArmRejection.UnsupportedSystem);
        if (_disposed) return Refuse(BackgroundArmRejection.Disposed);
        if (profile is null) return Refuse(BackgroundArmRejection.MissingTarget);
        if (!_backend.IsCurrent(profile.Original)) return Refuse(BackgroundArmRejection.TargetChanged);
        if (_state() != (prepareHidden ? GhostState.Ghost : GhostState.Reveal)) return Refuse(BackgroundArmRejection.InvalidState);
        if (IsDraining || _session.HasPendingReleases)
            return LogStart(BackgroundArmResult.Waiting(BackgroundArmStatus.AwaitingRelease), foreground, profile);
        if (_phase != BackgroundInteractionPhase.Idle) return Refuse(BackgroundArmRejection.Busy);
        if (ForegroundStatus(foreground, profile.Hwnd) == BackgroundArmStatus.AwaitingForeground)
            return LogStart(BackgroundArmResult.Waiting(BackgroundArmStatus.AwaitingForeground), foreground, profile);

        _generation++;
        _shortcuts = shortcuts;
        _prepareHidden = prepareHidden;
        _sessionEstablished = false;
        _peekVirtualKey = peekVirtualKey;
        BlocksReveal = false;
        _armedProfile = profile;
        _lastClick = null;
        if (!_hookThread.Invoke(() => _physicalKeys.Clear()))
            return Refuse(BackgroundArmRejection.Hooks);
        _borrowedKeys.Clear();
        _borrowedButtons.Clear();
        // Releases of these physical inputs still belong to the protected foreground.
        // Side-specific modifier codes avoid duplicate generic VK_CONTROL/SHIFT/ALT entries.
        for (var key = 8; key < 256; key++)
            if (key is not (0x10 or 0x11 or 0x12) && _windows.IsKeyDown(key)) _borrowedKeys.Add(key);
        foreach (var button in Enum.GetValues<BackgroundMouseButton>())
            if (_windows.IsKeyDown(ButtonKey(button))) _borrowedButtons.Add(button);
        _pending = profile;
        _pendingForeground = foreground;
        _pendingStarted = Stopwatch.GetTimestamp();
        _phase = BackgroundInteractionPhase.Preparing;
        _inputBlocked = false;
        _requestedStop = -1;
        _ownership.Begin(_borrowedKeys, _borrowedButtons);
        if (!InstallHooks())
        {
            Stop(BackgroundInteractionStopReason.DeliveryFailed);
            return LogStart(BackgroundArmResult.Reject(BackgroundArmRejection.Hooks, Win32NativeMethods.LastError), foreground, profile);
        }
        if (_backend.ForegroundWindow != _pendingForeground)
        {
            Stop(BackgroundInteractionStopReason.ForegroundChanged);
            return Refuse(BackgroundArmRejection.ForegroundChanged);
        }
        _logger.Log(LogLevel.Info, $"BackgroundInteraction phase=Preparing hwnd={profile.Hwnd} foreground={_pendingForeground} hidden={prepareHidden}");
        Preparing?.Invoke(_borrowedKeys.Count != 0 || _borrowedButtons.Count != 0);
        return LogStart(BackgroundArmResult.Waiting(BackgroundArmStatus.Preparing), foreground, profile);
    }

    private static BackgroundArmStatus ForegroundStatus(nint foreground, nint target)
    {
        var owner = foreground == 0 ? 0 : Win32NativeMethods.GetAncestor(foreground, 3); // GA_ROOTOWNER
        var belongs = owner != 0 && owner == Win32NativeMethods.GetAncestor(target, 3);
        return BackgroundStartPolicy.ForegroundStatus(foreground, target, belongs);
    }

    private BackgroundArmResult LogStart(BackgroundArmResult result, nint foreground, GhostWindowProfile? profile)
    {
        _logger.Log(result.Success ? LogLevel.Info : LogLevel.Warning,
            $"BackgroundStart status={result.Status} reason={result.Reason} state={_state()} foreground={foreground} target={profile?.Hwnd ?? 0} " +
            $"phase={_phase} releaseCount={_ownership.PendingCount} coreTails={_session.HasPendingReleases} " +
            $"elapsedMs={Stopwatch.GetElapsedTime(_armAttemptStarted).TotalMilliseconds:F1} foregroundRelation={(foreground == 0 ? "Absent" : profile is not null && ForegroundStatus(foreground, profile.Hwnd) == BackgroundArmStatus.AwaitingForeground ? "TargetOwned" : "Separate")}");
        return result;
    }

    public void Poll()
    {
        if (_disposed) return;
        foreach (var completion in _backend.DrainCompletions()) _session.CompleteInput(completion);
        // Hardware events have already been consumed. Only active-generation
        // work reaches the target, on the application thread rather than a hook.
        for (var count = 0; count < 256 && _inputQueue.TryDequeue(out var work); count++) work();
        var cursor = _windows.GetCursorPosition();
        if (_pending is { } pending)
        {
            if (_profile()?.Original != pending.Original || !_backend.IsCurrent(pending.Original))
                Stop(BackgroundInteractionStopReason.TargetChanged);
            else if (_backend.ForegroundWindow != _pendingForeground)
                Stop(BackgroundInteractionStopReason.ForegroundChanged);
            else if (_state() != GhostState.Reveal && !(_prepareHidden && _state() == GhostState.Ghost))
                Stop(BackgroundInteractionStopReason.RevealEnded);
            else if (!pending.Original.ScreenBounds.Contains(cursor))
                Stop(BackgroundInteractionStopReason.CursorLeft);
            else if (Stopwatch.GetElapsedTime(_pendingStarted) > TimeSpan.FromSeconds(5))
                Stop(BackgroundInteractionStopReason.DeliveryFailed);
            else if (!AnyInputDown() && _ownership.PendingCount == 0)
            {
                _pending = null;
                _hookThread.Invoke(() => _physicalKeys.Clear());
                var generation = _generation;
                var result = _session.Start(pending, allowHidden: _prepareHidden, expectedForeground: _pendingForeground);
                if (generation != _generation)
                {
                    // A reentrant foreground/recovery event cancelled preparation.
                    // Never resurrect the cancelled lease after a synchronous Win32 call.
                    if (_session.IsActive) _session.Stop(BackgroundInteractionStopReason.ForegroundChanged);
                }
                else if (result.Success)
                {
                    _sessionEstablished = true;
                    _phase = BackgroundInteractionPhase.Ready;
                    _logger.Log(LogLevel.Info, $"BackgroundInteraction phase=Ready hwnd={pending.Hwnd} foreground={_pendingForeground} elapsedMs={Stopwatch.GetElapsedTime(_pendingStarted).TotalMilliseconds:F1}");
                    if (!_prepareHidden) ActivateReveal();
                }
                else if (generation == _generation && _phase == BackgroundInteractionPhase.Preparing)
                {
                    _logger.Log(LogLevel.Warning, $"BackgroundInteraction start failed operation={result.Operation} error={result.ErrorCode}");
                    NotifyStopped(result.Operation switch
                    {
                        "BackgroundInteractionCleanup" => BackgroundInteractionStopReason.CleanupFailed,
                        "BackgroundForegroundChanged" => BackgroundInteractionStopReason.ForegroundChanged,
                        "BackgroundTargetChanged" or "BackgroundTargetMissing" => BackgroundInteractionStopReason.TargetChanged,
                        _ => BackgroundInteractionStopReason.DeliveryFailed
                    });
                }
            }
        }
        if (_session.IsActive)
            _session.Check(_state(), _profile(), cursor, allowHidden: IsReady);
        if ((IsActive || IsReady) && _armedProfile is { } active && _backend.HasUnmanagedPopup(active.Original))
            Stop(BackgroundInteractionStopReason.UnsupportedInput);

        if (IsReady)
        {
            if (Stopwatch.GetElapsedTime(_pendingStarted) > TimeSpan.FromSeconds(5))
                Stop(BackgroundInteractionStopReason.DeliveryFailed);
            else if (!_prepareHidden) ActivateReveal();
        }
        if (IsDraining && !_hooksInstalled)
        {
            _session.DiscardReleaseTails();
            _phase = BackgroundInteractionPhase.Idle;
        }

    }

    // Called synchronously after the coordinator has shown the prepared target.
    // Until this point the hooks only consume new input; nothing is replayed.
    public bool ActivateReveal()
    {
        if (!IsReady || _inputBlocked || _ownership.PendingCount != 0 || AnyInputDown() ||
            !_session.Check(_state(), _profile(), _windows.GetCursorPosition()) || !_ownership.Activate()) return false;
        if (_inputBlocked) return false;
        _phase = BackgroundInteractionPhase.Active;
        _logger.Log(LogLevel.Info, $"BackgroundInteraction active hwnd={_armedProfile?.Hwnd} foreground={_pendingForeground}");
        Started?.Invoke();
        return true;
    }

    public void Stop(BackgroundInteractionStopReason reason = BackgroundInteractionStopReason.User)
    {
        if (!IsActive && !IsPending) return;
        var requested = Interlocked.Exchange(ref _requestedStop, -1);
        if (requested >= 0) reason = (BackgroundInteractionStopReason)requested;
        _inputBlocked = true;
        _ownership.BeginDrain(DrainClock);
        while (_inputQueue.TryDequeue(out _)) { }
        _pending = null;
        if (_session.IsActive) _session.Stop(reason);
        else NotifyStopped(reason);
    }

    public void RecordTargetInputState(string phase)
    {
        if (_armedProfile is { } profile) _backend.TraceInputState(profile.Original, phase);
    }

    public void AcknowledgeRevealBlock() => BlocksReveal = false;

    private void NotifyStopped(BackgroundInteractionStopReason reason)
    {
        _inputBlocked = true;
        _ownership.BeginDrain(DrainClock);
        while (_inputQueue.TryDequeue(out _)) { }
        _pending = null;
        var cleanup = _sessionEstablished ? _session.LastCleanup : default;
        _logger.Log(LogLevel.Info, $"BackgroundInteraction generation={_generation} phase={_phase}->Draining reason={reason} " +
            $"owners={_ownership.Counts} targetPostedKeys={cleanup.PostedKeys} targetMouseDowns={cleanup.MouseButtons}");
        _phase = BackgroundInteractionPhase.Draining;
        var generation = ++_generation;
        var natural = _prepareHidden && reason is BackgroundInteractionStopReason.RevealEnded or BackgroundInteractionStopReason.CursorLeft;
        if (!_disposed && !natural) BlocksReveal = true;
        if (!_disposed && _state() == GhostState.Reveal && _armedProfile is { } armed &&
            _profile()?.Original == armed.Original)
            _backend.HideTargetAfterStop(armed.Original);
        _pendingHide = false;
        // Restore sets Restoring first; never append a hide after its snapshot restore.
        _dispatch(() => { if (generation == _generation && !_disposed) Stopped?.Invoke(reason); });
    }

    private static int ButtonKey(BackgroundMouseButton button) => button switch
    {
        BackgroundMouseButton.Left => 1, BackgroundMouseButton.Right => 2,
        BackgroundMouseButton.Middle => 4, BackgroundMouseButton.X1 => 5, _ => 6
    };

    private void DrainTick()
    {
        if (_passThrough && _hooksInstalled) { UninstallHooksCore(); return; }
        var pending = _ownership.PendingCount;
        if (!_ownership.DrainFinished(DrainClock, out var expired)) return;
        _passThrough = true;
        UninstallHooksCore();
        _logger.Log(expired ? LogLevel.Warning : LogLevel.Info,
            $"BackgroundInputDrain generation={_generation} expired={expired} remainingBefore={pending} pending={_ownership.PendingCount} hooks={_hooksInstalled}");
    }

    private void RequestStop(BackgroundInteractionStopReason reason)
    {
        if (_inputBlocked) return;
        Volatile.Write(ref _requestedStop, (int)reason);
        _inputBlocked = true;
        _pendingHide = true;
        _ownership.BeginDrain(DrainClock);
        var natural = _prepareHidden && reason is BackgroundInteractionStopReason.CursorLeft or BackgroundInteractionStopReason.RevealEnded;
        if (!natural) BlocksReveal = true;
        var generation = _generation;
        _dispatch(() => { if (generation == _generation) Stop(reason); });
    }

    private void EnqueueInput(Action work)
    {
        var generation = _generation;
        if (_inputQueue.Count >= 512) { RequestStop(BackgroundInteractionStopReason.DeliveryFailed); return; }
        _inputQueue.Enqueue(() =>
        {
            if (_inputBlocked || generation != _generation || !IsActive) return;
            try { work(); }
            catch (Exception error)
            {
                _logger.Log(LogLevel.Error, "Background input dispatch failed.", error);
                Stop(BackgroundInteractionStopReason.DeliveryFailed);
            }
        });
    }

    private bool InstallHooks()
    {
        var installed = false;
        var generation = _generation;
        return _hookThread.Invoke(() => installed = InstallHooksCore(generation)) && installed;
    }

    private bool InstallHooksCore(int generation)
    {
        UninstallHooksCore();
        if (generation != Volatile.Read(ref _generation) || !IsPending) return false;
        var module = Win32NativeMethods.GetModuleHandle(null);
        _keyboardHook = Win32NativeMethods.SetWindowsHookEx(Win32NativeMethods.WH_KEYBOARD_LL, _keyboardCallback, module, 0);
        _mouseHook = Win32NativeMethods.SetWindowsHookEx(Win32NativeMethods.WH_MOUSE_LL, _mouseCallback, module, 0);
        _foregroundHook = Win32BackgroundInputNative.SetWinEventHook(
            Win32BackgroundInputNative.EventSystemForeground, Win32BackgroundInputNative.EventSystemForeground,
            0, _foregroundCallback, 0, 0, 0);
        _hooksInstalled = _keyboardHook != 0 && _mouseHook != 0 && _foregroundHook != 0;
        if (generation != Volatile.Read(ref _generation) || !IsPending)
        {
            _passThrough = true;
            UninstallHooksCore();
            return false;
        }
        _inputBlocked = !_hooksInstalled;
        _passThrough = !_hooksInstalled;
        return _hooksInstalled;
    }

    private nint KeyboardCallback(int code, nint message, nint data)
    {
        if (code < 0 || _passThrough) return Win32NativeMethods.CallNextHookEx(_keyboardHook, code, message, data);
        var packet = Marshal.PtrToStructure<Win32NativeMethods.KBDLLHOOKSTRUCT>(data);
        var down = message == Win32NativeMethods.WM_KEYDOWN || message == Win32NativeMethods.WM_SYSKEYDOWN;
        var key = packet.VkCode switch
        {
            0x10 => packet.ScanCode == 0x36 ? 0xA1 : 0xA0,
            0x11 => (packet.Flags & 1) != 0 ? 0xA3 : 0xA2,
            0x12 => (packet.Flags & 1) != 0 ? 0xA5 : 0xA4,
            _ => (int)packet.VkCode
        };
        var input = new BackgroundKeyInput(key, packet.ScanCode, down, (packet.Flags & 1) != 0,
            message == Win32NativeMethods.WM_SYSKEYDOWN || message == Win32NativeMethods.WM_SYSKEYUP);
        try
        {
            if (!_inputBlocked && (IsActive || IsPending) && _backend.ForegroundWindow != _pendingForeground)
                RequestStop(BackgroundInteractionStopReason.ForegroundChanged);
            if (!_inputBlocked && (IsActive || IsPending) && !_ownership.IsForegroundKey(key))
            {
                var first = down ? _physicalKeys.Add(key) : _physicalKeys.Remove(key);
                if (down && first && MatchesPeek(key))
                {
                    _ownership.ConsumeKey(key);
                    RequestStop(BackgroundInteractionStopReason.PeekClosed);
                    return 1;
                }
                if (down && FindShortcut(key) is { } id)
                {
                    _ownership.ConsumeKey(key);
                    RequestStop(BackgroundInteractionStopReason.User);
                    _dispatch(() => ShortcutPressed?.Invoke(id));
                    return 1;
                }
            }
            var route = _ownership.Key(input);
            if (_pendingHide && down && route == BackgroundInputRoute.Foreground)
            {
                _ownership.ConsumeKey(key);
                return 1;
            }
            if (route == BackgroundInputRoute.Deliver)
                EnqueueInput(() =>
                {
                    var cursor = _windows.GetCursorPosition();
                    if (_session.Check(_state(), _profile(), cursor)) _session.HandleKey(input, cursor);
                });
            if (route != BackgroundInputRoute.Foreground) return 1;
        }
        catch
        {
            RequestStop(BackgroundInteractionStopReason.DeliveryFailed);
            return 1;
        }
        return Win32NativeMethods.CallNextHookEx(_keyboardHook, code, message, data);
    }

    private bool MatchesPeek(int key) => !_physicalKeys.Any(held => held != key && held is 0x11 or 0xA2 or 0xA3 or 0x12 or 0xA4 or 0xA5 or 0x10 or 0xA0 or 0xA1) &&
        (key == _peekVirtualKey || _peekVirtualKey switch
    {
        0x10 => key is 0xA0 or 0xA1,
        0x11 => key is 0xA2 or 0xA3,
        0x12 => key is 0xA4 or 0xA5,
        _ => false
    });

    private int? FindShortcut(int key)
    {
        var modifiers = ShortcutModifiers.None;
        if (_physicalKeys.Overlaps([0x11, 0xA2, 0xA3])) modifiers |= ShortcutModifiers.Control;
        if (_physicalKeys.Overlaps([0x12, 0xA4, 0xA5])) modifiers |= ShortcutModifiers.Alt;
        if (_physicalKeys.Overlaps([0x10, 0xA0, 0xA1])) modifiers |= ShortcutModifiers.Shift;
        foreach (var (id, binding) in _shortcuts)
        {
            if (!binding.IsDisabled && binding.VirtualKey == key && binding.Modifiers == modifiers) return id;
        }
        return null;
    }

    private nint MouseCallback(int code, nint message, nint data)
    {
        if (code < 0 || _passThrough) return Win32NativeMethods.CallNextHookEx(_mouseHook, code, message, data);
        var packet = Marshal.PtrToStructure<Win32NativeMethods.MSLLHOOKSTRUCT>(data);
        var cursor = new Point(packet.Point.X, packet.Point.Y);
        var button = MouseButton((int)message, packet.MouseData);
        var up = (int)message is 0x0202 or 0x0205 or 0x0208 or 0x020C;
        var move = (int)message == 0x0200;
        var wheel = (int)message is 0x020A or 0x020E;
        try
        {
            if (!_inputBlocked && (IsActive || IsPending) && _backend.ForegroundWindow != _pendingForeground)
                RequestStop(BackgroundInteractionStopReason.ForegroundChanged);
            if (!_inputBlocked && (IsActive || IsPending) && _armedProfile?.Original.ScreenBounds.Contains(cursor) != true)
                RequestStop(BackgroundInteractionStopReason.CursorLeft);
            var route = _ownership.Mouse(button, up, move);
            if (_pendingHide && !move && route == BackgroundInputRoute.Foreground && (!up || wheel))
            {
                if (button is { } blocked) _ownership.ConsumeMouse(blocked);
                return 1;
            }
            if (!move) _queuedMove = null; // Button/wheel boundaries seal the previous move group.
            if (route == BackgroundInputRoute.Deliver)
            {
                if (move) EnqueueMove(packet, cursor, message);
                else EnqueueInput(() => ProcessMouse(packet, cursor, button, up, false, wheel, message));
            }
            if (!move && route != BackgroundInputRoute.Foreground) return 1;
        }
        catch
        {
            RequestStop(BackgroundInteractionStopReason.DeliveryFailed);
            if (!move) return 1;
        }
        return Win32NativeMethods.CallNextHookEx(_mouseHook, code, message, data);
    }

    private void EnqueueMove(Win32NativeMethods.MSLLHOOKSTRUCT packet, Point cursor, nint message)
    {
        if (_queuedMove is { } pending)
        {
            lock (pending)
            {
                if (!pending.Taken)
                {
                    pending.Packet = packet; pending.Cursor = cursor;
                    pending.Motion = pending.Motion.Include(BackgroundMouseMotion.At(cursor));
                    return;
                }
            }
        }
        var next = new QueuedMove { Packet = packet, Cursor = cursor, Motion = BackgroundMouseMotion.At(cursor) };
        _queuedMove = next;
        EnqueueInput(() =>
        {
            Win32NativeMethods.MSLLHOOKSTRUCT latest;
            Point position;
            BackgroundMouseMotion motion;
            lock (next) { next.Taken = true; latest = next.Packet; position = next.Cursor; motion = next.Motion; }
            ProcessMouse(latest, position, null, false, true, false, message, motion);
        });
    }

    private void ProcessMouse(Win32NativeMethods.MSLLHOOKSTRUCT packet, Point cursor, BackgroundMouseButton? button,
        bool up, bool move, bool wheel, nint message, BackgroundMouseMotion? motion = null)
    {
        if (!_session.Check(_state(), _profile(), cursor) || _profile() is not { } profile) return;
        if (move)
        {
            if (_session.HasHeldMouseButtons) _session.HandleMouse(new(BackgroundMouseAction.Move, Motion: motion), cursor, 0);
            return;
        }
        var recipient = up ? 0 : _backend.ResolveMouseTarget(profile.Original, cursor);
        var doubleClick = false;
        if (button is { } pressed && !up && recipient != 0)
        {
            _backend.RememberClick(recipient);
            doubleClick = IsDoubleClick(pressed, recipient, cursor, packet.Time);
        }
        var action = wheel ? (int)message == 0x020A ? BackgroundMouseAction.Wheel : BackgroundMouseAction.HorizontalWheel
            : up ? BackgroundMouseAction.Up : BackgroundMouseAction.Down;
        _session.HandleMouse(new(action, button, unchecked((short)(packet.MouseData >> 16)), doubleClick), cursor, recipient);
    }

    private bool IsDoubleClick(BackgroundMouseButton button, nint recipient, Point cursor, uint time)
    {
        var doubleClick = _lastClick is { } last && last.Button == button && last.Recipient == recipient &&
            unchecked(time - last.Time) <= Win32BackgroundInputNative.GetDoubleClickTime() &&
            Math.Abs((long)cursor.X - last.Cursor.X) <= Win32BackgroundInputNative.GetSystemMetrics(36) / 2 &&
            Math.Abs((long)cursor.Y - last.Cursor.Y) <= Win32BackgroundInputNative.GetSystemMetrics(37) / 2;
        _lastClick = doubleClick ? null : (button, recipient, cursor, time);
        return doubleClick;
    }

    private static BackgroundMouseButton? MouseButton(int message, uint data) => message switch
    {
        0x0201 or 0x0202 or 0x0203 => BackgroundMouseButton.Left,
        0x0204 or 0x0205 or 0x0206 => BackgroundMouseButton.Right,
        0x0207 or 0x0208 or 0x0209 => BackgroundMouseButton.Middle,
        0x020B or 0x020C or 0x020D => (data >> 16) == 1 ? BackgroundMouseButton.X1 : BackgroundMouseButton.X2,
        _ => null
    };

    private void OnForegroundChanged(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint threadId, uint time)
    {
        if (!_inputBlocked && (IsActive || IsPending) && hwnd != _pendingForeground)
        {
            RequestStop(BackgroundInteractionStopReason.ForegroundChanged);
            _dispatch(() => _logger.Log(LogLevel.Info, $"BackgroundForegroundEvent hwnd={hwnd} eventTimeMs={time}"));
        }
    }

    private static bool AnyInputDown()
    {
        for (var key = 1; key < 256; key++)
        {
            if ((Win32NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0) return true;
        }
        return false;
    }

    private void UninstallHooksCore()
    {
        var keyboard = UnhookInput(_keyboardHook);
        var mouse = UnhookInput(_mouseHook);
        var foreground = _foregroundHook == 0 || Win32BackgroundInputNative.UnhookWinEvent(_foregroundHook);
        if (!foreground && Win32NativeMethods.LastError == 6) foreground = true; // Already removed event handle.
        // Even an unhook failure must never leave a callback consuming input.
        _inputBlocked = true;
        if (keyboard) _keyboardHook = 0;
        if (mouse) _mouseHook = 0;
        if (foreground) _foregroundHook = 0;
        _hooksInstalled = _keyboardHook != 0 || _mouseHook != 0 || _foregroundHook != 0;
        _logger.Log(keyboard && mouse && foreground ? LogLevel.Info : LogLevel.Warning,
            $"BackgroundHooksUninstalled generation={_generation} keyboard={keyboard} mouse={mouse} foreground={foreground}");
    }

    private static bool UnhookInput(nint hook) => hook == 0 || Win32NativeMethods.UnhookWindowsHookEx(hook) ||
        Win32NativeMethods.LastError == 1404; // ERROR_INVALID_HOOK_HANDLE: Windows may already have removed a timed-out hook.

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _hookThread.Invoke(UninstallHooksCore);
        _hookThread.Dispose();
        _backend.Dispose();
    }
}
