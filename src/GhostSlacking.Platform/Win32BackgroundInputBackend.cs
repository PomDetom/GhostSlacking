using System.Collections.Concurrent;
using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

internal sealed class Win32BackgroundInputBackend(IWindowApi windows, ILogger logger) : IBackgroundInteractionBackend, IDisposable
{
    private readonly byte[] _keyboard = new byte[256];
    private readonly ConcurrentDictionary<nint, (uint Process, uint Thread, string Class)> _recipients = [];
    private readonly Dictionary<nint, IBackgroundControlAdapter> _adapters = [];
    private readonly Dictionary<int, (nint Recipient, BackgroundKeyInput Input)> _rawKeys = [];
    private nint _clicked;
    private nint _layout;
    private uint _mouseFlags;
    private nint _protectedForeground;
    private bool _scintillaTarget;
    private bool _semanticTarget;
    private int _inputEpoch;
    private long _inputGeneration;
    private bool _shiftTap;
    private readonly IBackgroundTextInputEngine _inputEngine = new BackgroundEnglishInputEngine();
    private WindowSnapshot? _diagnosticSnapshot;
    private readonly BackgroundAutomationText _automation = new(logger);
    private (nint Recipient, BackgroundInputCategory Category, BackgroundInputRejectionReason? Rejection)? _lastTrace;

    public nint ForegroundWindow => Win32BackgroundInputNative.GetForegroundWindow();

    internal bool IsCurrent(WindowSnapshot snapshot)
    {
        var observed = windows.Observe(snapshot.Hwnd);
        return observed is not null && windows.IsSameIdentity(snapshot, observed);
    }

    public NativeResult SetNoActivate(WindowSnapshot snapshot, bool restore)
    {
        if (!IsCurrent(snapshot))
        {
            return NativeResult.Failed("BackgroundStyleIdentity", 0, "The registered target identity changed.");
        }
        var hwnd = snapshot.Hwnd;
        var current = Win32NativeMethods.GetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE);
        var mask = (long)Win32NativeMethods.WS_EX_NOACTIVATE;
        var desired = restore ? (long)snapshot.Styles.ExtendedStyle & mask : mask;
        var updated = (nint)(((long)current & ~mask) | desired);
        Win32NativeMethods.SetLastError(0);
        Win32NativeMethods.SetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE, updated);
        var error = Win32NativeMethods.LastError;
        if (error != 0 || Win32NativeMethods.GetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE) != updated)
        {
            logger.Log(LogLevel.Warning, $"BackgroundInteraction style failed hwnd={hwnd} restore={restore} error={error}");
            return NativeResult.Failed("BackgroundStyle", error, "Could not verify the non-activation style.");
        }
        if (!restore)
        {
            ResetInputState();
            Array.Clear(_keyboard);
            _keyboard[0x14] = (byte)(Win32BackgroundInputNative.GetKeyState(0x14) & 1);
            _keyboard[0x90] = (byte)(Win32BackgroundInputNative.GetKeyState(0x90) & 1);
            _recipients.Clear();
            _adapters.Clear();
            _rawKeys.Clear();
            _lastTrace = null;
            _clicked = 0;
            _mouseFlags = 0;
            _protectedForeground = ForegroundWindow;
            _diagnosticSnapshot = snapshot;
            TraceInputState(snapshot, "prepare");
            _layout = BackgroundEnglishKeyboard.FindLoadedUsLayout();
            _scintillaTarget = false;
            _semanticTarget = UsesSemanticText(ClassName(hwnd));
            RememberRecipient(hwnd);
            if (_semanticTarget)
            {
                var discovered = _automation.Prepare(hwnd, Access(snapshot, hwnd).Validate, _inputGeneration);
                logger.Log(LogLevel.Info, $"BackgroundTextDiscovery hwnd={hwnd} semantic=True textSelection={_automation.HasTextSelection} success={discovered.Result.Success} operation={discovered.Result.Operation}");
            }
            Win32BackgroundInputNative.EnumChildWindows(hwnd, (child, _) =>
            {
                var childClass = ClassName(child);
                if (childClass == "Scintilla") _scintillaTarget = true;
                if (childClass is "Scintilla" or "Edit" || childClass.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase))
                    RememberRecipient(child);
                return true;
            }, 0);
            logger.Log(LogLevel.Info, $"BackgroundInputPrepared hwnd={hwnd} keyboard=US-English translation={(_layout == 0 ? "FixedTable" : "LoadedLayout")} scintilla={_scintillaTarget}");
        }
        return NativeResult.Ok("BackgroundStyle");
    }

    internal nint ResolveMouseTarget(WindowSnapshot snapshot, Point cursor)
    {
        if (!IsCurrent(snapshot) || HasUnmanagedPopup(snapshot))
        {
            return 0;
        }
        // Resolve inside the registered window's child tree, independent of the
        // last frame's region. WindowFromPoint can hit A while Reveal catches up.
        var recipient = snapshot.Hwnd;
        for (var depth = 0; depth < 32; depth++)
        {
            if (!TryClientPoint(recipient, cursor, out var local) ||
                !Win32NativeMethods.GetClientRect(recipient, out var client) ||
                local.X < client.Left || local.X >= client.Right || local.Y < client.Top || local.Y >= client.Bottom ||
                !Win32BackgroundInputNative.IsWindowEnabled(recipient)) return 0;
            var child = Win32BackgroundInputNative.ChildWindowFromPointEx(recipient, local, 1 | 2 | 4);
            if (child == 0 || child == recipient)
            {
                RememberRecipient(recipient);
                return recipient;
            }
            if (!BelongsToTarget(snapshot, child)) return 0;
            recipient = child;
        }
        return 0;
    }

    internal bool HasUnmanagedPopup(WindowSnapshot snapshot)
    {
        var popup = Win32BackgroundInputNative.GetLastActivePopup(snapshot.Hwnd);
        if (popup != 0 && popup != snapshot.Hwnd && Win32NativeMethods.IsWindowVisible(popup)) return true;
        if (!Win32BackgroundInputNative.IsWindowEnabled(snapshot.Hwnd)) return true;
        var thread = Win32NativeMethods.GetWindowThreadProcessId(snapshot.Hwnd, out _);
        var info = new Win32BackgroundInputNative.GuiThreadInfo
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32BackgroundInputNative.GuiThreadInfo>()
        };
        return Win32BackgroundInputNative.GetGUIThreadInfo(thread, ref info) && (info.Flags & 0x1C) != 0;
    }

    internal void HideTargetAfterStop(WindowSnapshot snapshot)
    {
        if (!IsCurrent(snapshot)) return;
        // Complete the hide before a later Restore can show the window again.
        // An asynchronous hide could otherwise arrive after Restore.
        Win32NativeMethods.ShowWindow(snapshot.Hwnd, Win32NativeMethods.SW_HIDE);
        if (IsCurrent(snapshot) && Win32NativeMethods.IsWindowVisible(snapshot.Hwnd))
        {
            logger.Log(LogLevel.Warning, $"BackgroundInteraction target remained visible after hide hwnd={snapshot.Hwnd}");
        }
    }

    internal void RememberClick(nint recipient) => _clicked = recipient;

    public nint ResolveKeyboardTarget(WindowSnapshot snapshot, Point cursor)
    {
        if (ValidKeyboardRecipient(snapshot, _clicked))
        {
            return _clicked;
        }
        var thread = Win32NativeMethods.GetWindowThreadProcessId(snapshot.Hwnd, out _);
        var info = new Win32BackgroundInputNative.GuiThreadInfo
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32BackgroundInputNative.GuiThreadInfo>()
        };
        if (Win32BackgroundInputNative.GetGUIThreadInfo(thread, ref info) && BelongsToTarget(snapshot, info.Focus) &&
            (!_scintillaTarget || ClassName(info.Focus) == "Scintilla"))
        {
            RememberRecipient(info.Focus);
            return info.Focus;
        }
        var hit = ResolveMouseTarget(snapshot, cursor);
        return ValidKeyboardRecipient(snapshot, hit) ? hit : 0;
    }

    public NativeResult PostKey(WindowSnapshot snapshot, nint recipient, BackgroundKeyInput input)
    {
        UpdateKeyboardState(input);
        return DispatchKey(snapshot, recipient, input).Result;
    }

    public void UpdateKeyboardState(BackgroundKeyInput input)
    {
        var key = input.VirtualKey;
        if (input.IsDown)
        {
            if (key is 0xA0 or 0xA1) _shiftTap = true;
            else _shiftTap = false;
        }
        if (key is < 1 or > 255) return;
        var repeated = (_keyboard[key] & 0x80) != 0;
        _keyboard[key] = (byte)((_keyboard[key] & 1) | (input.IsDown ? 0x80 : 0));
        if (key is 0x10 or 0x11 or 0x12)
        {
            var sided = key == 0x10 ? input.ScanCode == 0x36 ? 0xA1 : 0xA0
                : key == 0x11 ? input.Extended ? 0xA3 : 0xA2
                : input.Extended ? 0xA5 : 0xA4;
            _keyboard[sided] = _keyboard[key];
        }
        if (input.IsDown && !repeated && key is 0x14 or 0x90)
        {
            _keyboard[key] ^= 1;
        }
        UpdateModifiers();
    }

    public BackgroundInputDispatchResult DispatchKey(WindowSnapshot snapshot, nint recipient, BackgroundKeyInput input) =>
        Trace(recipient, DispatchKeyCore(snapshot, recipient, input));

    private BackgroundInputDispatchResult DispatchKeyCore(WindowSnapshot snapshot, nint recipient, BackgroundKeyInput input)
    {
        var valid = GuardRecipient(snapshot, recipient, input.IsDown);
        if (!valid.Success) return new(valid, BackgroundInputCategory.RawKey);
        var key = input.VirtualKey;
        if (!input.IsDown)
        {
            if (key is 0xA0 or 0xA1 && _shiftTap)
            {
                _shiftTap = false;
                return _inputEngine.RequestLanguageSwitch();
            }
            if (!_rawKeys.TryGetValue(key, out var held)) return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
            if (held.Recipient != recipient)
                return new(NativeResult.Failed("BackgroundKeyIdentity", 0, "The key recipient changed."), BackgroundInputCategory.RawKey);
            var up = held.Input with { IsDown = false };
            if (!Win32BackgroundInputNative.PostMessage(recipient, 0x0101, MessageVirtualKey(key), KeyMessageParameter(up, true, false)))
                return new(DeliveryFailure("BackgroundKeyRelease"), BackgroundInputCategory.RawKey);
            _rawKeys.Remove(key);
            return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.RawKey);
        }
        if (key is < 1 or > 255) return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.UnsupportedKey);
        if (HasUnmanagedPopup(snapshot)) return new(Unsupported(), BackgroundInputCategory.RawKey);
        if (HotkeyBinding.IsModifierVirtualKey(key) || key is 0x14 or 0x90 or 0x91)
            return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Modifier);
        if (key is 0xE5 or 0xE7 or 0x15 or 0x19 or 0x1C or 0x1D)
            return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.ImeComposition);
        var modifiers = Modifiers;
        if (key == 0x20 && (modifiers.HasFlag(ShortcutModifiers.Control) || (_keyboard[0x5B] & 0x80) != 0 || (_keyboard[0x5C] & 0x80) != 0))
            return _inputEngine.RequestLanguageSwitch();
        if (key is 0x5B or 0x5C || (_keyboard[0x5B] & 0x80) != 0 || (_keyboard[0x5C] & 0x80) != 0 ||
            modifiers.HasFlag(ShortcutModifiers.Alt))
            return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.SystemShortcut);

        if (Adapter(recipient) is { } adapter)
        {
            var adapted = adapter.DispatchKey(Access(snapshot, recipient), input, modifiers);
            if (adapted is { } handled) return handled;
        }
        else if (_scintillaTarget)
            return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.NoInputRecipient);

        if (modifiers.HasFlag(ShortcutModifiers.Control))
            return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.UnsupportedKey);
        if (ScintillaBackgroundControlAdapter.IsPrintableKey(key))
        {
            var text = new char[8];
            int characters;
            if (_layout != 0)
                characters = Win32BackgroundInputNative.ToUnicodeEx((uint)key, input.ScanCode, _keyboard, text, text.Length, 4, _layout);
            else if (BackgroundEnglishKeyboard.TryTranslate(key, modifiers.HasFlag(ShortcutModifiers.Shift), (_keyboard[0x14] & 1) != 0, out var mapped))
            {
                text[0] = mapped;
                characters = 1;
            }
            else characters = 0;
            if (characters < 0) return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.DeadKey);
            if (characters == 0) return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.UnsupportedKey);
            if (characters > text.Length)
                return new(NativeResult.Failed("BackgroundCharacterTranslation", 0, "Invalid character count."), BackgroundInputCategory.Character);
            for (var index = 0; index < characters; index++)
            {
                valid = GuardRecipient(snapshot, recipient);
                if (!valid.Success) return new(valid, BackgroundInputCategory.Character);
                if (!Win32BackgroundInputNative.PostMessage(recipient, 0x0102, text[index], KeyMessageParameter(input, input.IsRepeat, false)))
                    return new(DeliveryFailure("BackgroundCharacter"), BackgroundInputCategory.Character);
            }
            return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Character);
        }
        if (key is not (0x08 or 0x09 or 0x0D or 0x1B or >= 0x21 and <= 0x28 or 0x2D or 0x2E) ||
            modifiers.HasFlag(ShortcutModifiers.Shift))
            return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.UnsupportedKey);
        var alreadyDown = _rawKeys.ContainsKey(key);
        if (!Win32BackgroundInputNative.PostMessage(recipient, 0x0100, MessageVirtualKey(key), KeyMessageParameter(input, input.IsRepeat, false)))
            return new(DeliveryFailure("BackgroundKey"), BackgroundInputCategory.RawKey, alreadyDown);
        _rawKeys[key] = (recipient, input);
        return new(NativeResult.Ok("BackgroundKey"), BackgroundInputCategory.RawKey, true);
    }

    internal static bool IsCompositionLayout(nint layout) => ((long)layout & 0x3FF) is 0x04 or 0x11 or 0x12;

    internal static int MessageVirtualKey(int key) => key switch
    {
        0xA0 or 0xA1 => 0x10,
        0xA2 or 0xA3 => 0x11,
        0xA4 or 0xA5 => 0x12,
        _ => key
    };

    internal static nint KeyMessageParameter(BackgroundKeyInput input, bool repeated, bool alt)
    {
        var bits = 1U | ((input.ScanCode & 0xFF) << 16);
        if (input.Extended) bits |= 1U << 24;
        if (input.SystemKey && alt) bits |= 1U << 29;
        if (repeated || !input.IsDown) bits |= 1U << 30;
        if (!input.IsDown) bits |= 1U << 31;
        return (nint)bits;
    }

    public NativeResult PostMouse(WindowSnapshot snapshot, nint recipient, BackgroundMouseInput input, Point cursor)
        => DispatchMouse(snapshot, recipient, input, cursor).Result;

    public BackgroundInputDispatchResult DispatchMouse(WindowSnapshot snapshot, nint recipient, BackgroundMouseInput input, Point cursor) =>
        Trace(recipient, DispatchMouseCore(snapshot, recipient, input, cursor));

    private BackgroundInputDispatchResult DispatchMouseCore(WindowSnapshot snapshot, nint recipient, BackgroundMouseInput input, Point cursor)
    {
        var valid = GuardRecipient(snapshot, recipient, input.Action != BackgroundMouseAction.Up);
        if (!valid.Success) return new(valid, BackgroundInputCategory.Mouse);
        if (input.Action != BackgroundMouseAction.Up && HasUnmanagedPopup(snapshot)) return new(Unsupported(), BackgroundInputCategory.Mouse);
        if (input.Action == BackgroundMouseAction.Down) _shiftTap = false;
        var previousFlags = _mouseFlags;
        var buttonMask = input.Button is { } button ? MouseButtonMask(button) : 0;
        if (input.Action == BackgroundMouseAction.Down) _mouseFlags |= buttonMask;
        if (input.Action == BackgroundMouseAction.Up) _mouseFlags &= ~buttonMask;
        if (Adapter(recipient) is { } adapter)
        {
            if (!TryClientPoint(recipient, cursor, out var client))
                return new(DeliveryFailure("BackgroundMouseCoordinates"), BackgroundInputCategory.Mouse);
            var adapted = adapter.DispatchMouse(Access(snapshot, recipient), input, new(client.X, client.Y), Modifiers);
            if (adapted is { } handled)
            {
                if (handled.Rejection is not null) _mouseFlags = previousFlags;
                return handled;
            }
        }
        else if (_scintillaTarget)
        {
            _mouseFlags = previousFlags;
            return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.OutsideEditor);
        }
        var flags = _mouseFlags;
        if ((_keyboard[0x10] & 0x80) != 0) flags |= 0x0004;
        if ((_keyboard[0x11] & 0x80) != 0) flags |= 0x0008;

        uint message;
        nint parameter;
        if (input.Action is BackgroundMouseAction.Wheel or BackgroundMouseAction.HorizontalWheel)
        {
            message = input.Action == BackgroundMouseAction.Wheel ? 0x020AU : 0x020EU;
            flags |= (uint)(ushort)input.WheelDelta << 16;
            parameter = PointParameter(cursor.X, cursor.Y);
        }
        else
        {
            if (!TryClientPoint(recipient, cursor, out var local)) return new(DeliveryFailure("BackgroundMouseCoordinates"), BackgroundInputCategory.Mouse);
            parameter = PointParameter(local.X, local.Y);
            var doubleClick = input.DoubleClick &&
                ((long)Win32BackgroundInputNative.GetClassLongPtr(recipient, -26) & 0x0008) != 0;
            message = input.Action switch
            {
                BackgroundMouseAction.Move => 0x0200U,
                BackgroundMouseAction.Down => input.Button switch
                {
                    BackgroundMouseButton.Left => doubleClick ? 0x0203U : 0x0201U,
                    BackgroundMouseButton.Right => doubleClick ? 0x0206U : 0x0204U,
                    BackgroundMouseButton.Middle => doubleClick ? 0x0209U : 0x0207U,
                    _ => doubleClick ? 0x020DU : 0x020BU
                },
                _ => input.Button switch
                {
                    BackgroundMouseButton.Left => 0x0202U,
                    BackgroundMouseButton.Right => 0x0205U,
                    BackgroundMouseButton.Middle => 0x0208U,
                    _ => 0x020CU
                }
            };
            if (input.Button is BackgroundMouseButton.X1 or BackgroundMouseButton.X2)
                flags |= (input.Button == BackgroundMouseButton.X1 ? 1U : 2U) << 16;
        }
        return new(Win32BackgroundInputNative.PostMessage(recipient, message, (nint)flags, parameter)
            ? NativeResult.Ok("BackgroundMouse") : DeliveryFailure("BackgroundMouse"), BackgroundInputCategory.Mouse);
    }

    private static uint MouseButtonMask(BackgroundMouseButton button) => button switch
    {
        BackgroundMouseButton.Left => 0x0001U,
        BackgroundMouseButton.Right => 0x0002U,
        BackgroundMouseButton.Middle => 0x0010U,
        BackgroundMouseButton.X1 => 0x0020U,
        _ => 0x0040U
    };

    private static nint PointParameter(int x, int y) => (nint)(((uint)(ushort)y << 16) | (ushort)x);

    public NativeResult ReleaseMouseButton(WindowSnapshot snapshot, nint recipient, BackgroundMouseButton button, Point cursor)
    {
        if (!IsCurrent(snapshot) || !ValidRecipient(snapshot, recipient))
            return NativeResult.Failed("BackgroundMouseIdentity", 0, "The cleanup recipient changed.");
        if (Adapter(recipient) is { } adapter)
        {
            adapter.ResetDrag();
            _mouseFlags &= ~MouseButtonMask(button);
            return NativeResult.Ok("BackgroundSelectionRelease");
        }
        var released = PostMouse(snapshot, recipient, new(BackgroundMouseAction.Up, button), cursor);
        if (!released.Success) return released;
        return IsCurrent(snapshot) && ValidRecipient(snapshot, recipient) &&
            Win32BackgroundInputNative.PostMessage(recipient, 0x001F, 0, 0) // WM_CANCELMODE releases standard-control capture.
            ? NativeResult.Ok("BackgroundMouseRelease") : DeliveryFailure("BackgroundMouseRelease");
    }

    internal BackgroundInputDispatchResult CommitText(WindowSnapshot snapshot, nint recipient, string text)
    {
        var valid = GuardRecipient(snapshot, recipient);
        if (!valid.Success) return new(valid, BackgroundInputCategory.Character);
        var adapter = Adapter(recipient);
        return adapter is not null && adapter.Capabilities.HasFlag(BackgroundTextCapabilities.CommitText)
            ? Trace(recipient, adapter.CommitText(Access(snapshot, recipient), text))
            : BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.UnsupportedKey);
    }

    private static bool TryClientPoint(nint hwnd, Point cursor, out Win32NativeMethods.POINT point)
    {
        point = new Win32NativeMethods.POINT { X = cursor.X, Y = cursor.Y };
        return Win32BackgroundInputNative.ScreenToClient(hwnd, ref point);
    }

    private static bool BelongsToTarget(WindowSnapshot snapshot, nint recipient) => recipient != 0 &&
        Win32NativeMethods.IsWindow(recipient) &&
        (recipient == snapshot.Hwnd || Win32BackgroundInputNative.IsChild(snapshot.Hwnd, recipient)) &&
        Win32NativeMethods.GetAncestor(recipient, Win32NativeMethods.GA_ROOT) == snapshot.Hwnd;

    private void RememberRecipient(nint recipient)
    {
        var thread = Win32NativeMethods.GetWindowThreadProcessId(recipient, out var process);
        var name = ClassName(recipient);
        if (_recipients.TryGetValue(recipient, out var previous) && previous == (process, thread, name)) return;
        _recipients[recipient] = (process, thread, name);
        _adapters.Remove(recipient);
        if (name == "Scintilla") _adapters[recipient] = new ScintillaBackgroundControlAdapter();
        else if (name == "Edit") _adapters[recipient] = new EditBackgroundControlAdapter();
        else if (name.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase))
            _adapters[recipient] = new AutomationBackgroundControlAdapter(_automation, recipient, _inputGeneration, richEdit: true);
        else if (_semanticTarget || UsesSemanticText(name))
            _adapters[recipient] = new AutomationBackgroundControlAdapter(_automation, Win32NativeMethods.GetAncestor(recipient, Win32NativeMethods.GA_ROOT), _inputGeneration);
        var info = new Win32BackgroundInputNative.GuiThreadInfo
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32BackgroundInputNative.GuiThreadInfo>()
        };
        Win32BackgroundInputNative.GetGUIThreadInfo(thread, ref info);
        logger.Log(LogLevel.Info, $"BackgroundControlSelected hwnd={recipient} class={name} adapter={Adapter(recipient)?.Name ?? "Generic"} targetFocus={info.Focus}");
    }

    private bool ValidRecipient(WindowSnapshot snapshot, nint recipient)
    {
        if (!BelongsToTarget(snapshot, recipient) || !_recipients.TryGetValue(recipient, out var identity)) return false;
        var thread = Win32NativeMethods.GetWindowThreadProcessId(recipient, out var process);
        return thread == identity.Thread && process == identity.Process && ClassName(recipient) == identity.Class;
    }

    private bool ValidKeyboardRecipient(WindowSnapshot snapshot, nint recipient) => ValidRecipient(snapshot, recipient) &&
        (!_scintillaTarget || _recipients[recipient].Class == "Scintilla");

    private static string ClassName(nint hwnd)
    {
        var buffer = new char[256];
        var count = Win32BackgroundInputNative.GetClassName(hwnd, buffer, buffer.Length);
        return count > 0 ? new string(buffer, 0, count) : string.Empty;
    }

    private IBackgroundControlAdapter? Adapter(nint recipient) => _adapters.GetValueOrDefault(recipient);

    private BackgroundControlAccess Access(WindowSnapshot snapshot, nint recipient)
    {
        var epoch = _inputEpoch;
        return new(recipient, () => epoch == Volatile.Read(ref _inputEpoch) ? GuardRecipient(snapshot, recipient)
            : NativeResult.Failed("BackgroundInputCancelled", 0, "The input session ended."));
    }

    internal static bool UsesSemanticText(string name) => name.StartsWith("Chrome_", StringComparison.Ordinal) ||
        name.StartsWith("Mozilla", StringComparison.Ordinal) || name.StartsWith("HwndWrapper", StringComparison.Ordinal) ||
        name.StartsWith("Windows.UI.", StringComparison.Ordinal) || name.StartsWith("WinUIDesktop", StringComparison.Ordinal);

    public void BeginInputSession(long generation) => _inputGeneration = generation;
    public IEnumerable<BackgroundInputCompletion> DrainCompletions() => _automation.DrainCompletions();

    public void ResetInputState()
    {
        Interlocked.Increment(ref _inputEpoch);
        _inputEngine.CancelComposition();
        if (_diagnosticSnapshot is { } snapshot) TraceInputState(snapshot, "reset");
        _diagnosticSnapshot = null;
        Array.Clear(_keyboard);
        _shiftTap = false;
        _mouseFlags = 0;
        _rawKeys.Clear();
        _clicked = 0;
        foreach (var adapter in _adapters.Values) adapter.ResetDrag();
        _automation.Reset();
    }

    internal void TraceInputState(WindowSnapshot snapshot, string phase)
    {
        if (!IsCurrent(snapshot)) return;
        var thread = Win32NativeMethods.GetWindowThreadProcessId(snapshot.Hwnd, out _);
        var info = new Win32BackgroundInputNative.GuiThreadInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32BackgroundInputNative.GuiThreadInfo>() };
        var observed = Win32BackgroundInputNative.GetGUIThreadInfo(thread, ref info);
        var ime = Win32BackgroundInputNative.ImmGetDefaultIMEWnd(snapshot.Hwnd);
        nint open = -1;
        var known = ime != 0 && Win32BackgroundInputNative.SendMessageTimeout(ime, 0x0283, 5, 0, 3, 30, out open) != 0;
        if (!known) open = -1;
        logger.Log(LogLevel.Info, $"BackgroundInputState phase={phase} hwnd={snapshot.Hwnd} foreground={ForegroundWindow} " +
            $"targetFocus={(observed ? info.Focus : 0)} layout={Win32BackgroundInputNative.GetKeyboardLayout(thread)} imeWindow={ime} imeQueryAvailable={known} imeOpen={open} engine={_inputEngine.Name}");
    }

    public void Dispose() => _automation.Dispose();

    private NativeResult GuardRecipient(WindowSnapshot snapshot, nint recipient, bool protectForeground = true)
    {
        if (!IsCurrent(snapshot) || !ValidRecipient(snapshot, recipient))
            return NativeResult.Failed("BackgroundInputIdentity", 0, "The input recipient identity changed.");
        if (protectForeground && ForegroundWindow != _protectedForeground)
            return NativeResult.Failed("BackgroundForegroundChanged", 0, "The protected foreground changed.");
        return NativeResult.Ok("BackgroundInputIdentity");
    }

    private ShortcutModifiers Modifiers =>
        ((_keyboard[0x10] & 0x80) != 0 ? ShortcutModifiers.Shift : 0) |
        ((_keyboard[0x11] & 0x80) != 0 ? ShortcutModifiers.Control : 0) |
        ((_keyboard[0x12] & 0x80) != 0 ? ShortcutModifiers.Alt : 0);

    private BackgroundInputDispatchResult Trace(nint recipient, BackgroundInputDispatchResult result)
    {
        if (result.Category == BackgroundInputCategory.Ignored && result.Rejection is null) return result;
        var current = (recipient, result.Category, result.Rejection);
        if (_lastTrace == current && result.Result.Success) return result;
        _lastTrace = current;
        var thread = Win32NativeMethods.GetWindowThreadProcessId(recipient, out _);
        var info = new Win32BackgroundInputNative.GuiThreadInfo
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32BackgroundInputNative.GuiThreadInfo>()
        };
        Win32BackgroundInputNative.GetGUIThreadInfo(thread, ref info);
        var name = _recipients.TryGetValue(recipient, out var identity) ? identity.Class : "Unknown";
        logger.Log(result.Result.Success || result.Rejection is not null ? LogLevel.Info : LogLevel.Warning,
            $"BackgroundInputDispatch hwnd={recipient} class={name} adapter={Adapter(recipient)?.Name ?? "Generic"} " +
            $"targetFocus={info.Focus} capabilities={Adapter(recipient)?.Capabilities ?? BackgroundTextCapabilities.None} category={result.Category} rejection={result.Rejection?.ToString() ?? "None"} " +
            $"operation={result.Result.Operation} error={result.Result.ErrorCode}");
        return result;
    }

    private void UpdateModifiers()
    {
        foreach (var (general, left, right) in new[] { (0x10, 0xA0, 0xA1), (0x11, 0xA2, 0xA3), (0x12, 0xA4, 0xA5) })
        {
            _keyboard[general] = (byte)((_keyboard[left] | _keyboard[right]) & 0x80);
        }
    }

    private static NativeResult Unsupported() => NativeResult.Failed("UnsupportedBackgroundInput", 0, "IME composition, dead keys and system input are unsupported in this prototype.");

    private NativeResult DeliveryFailure(string operation)
    {
        var error = Win32NativeMethods.LastError;
        logger.Log(LogLevel.Warning, $"BackgroundInteraction delivery failed operation={operation} error={error}");
        return NativeResult.Failed(operation, error, "Background input could not be posted.");
    }
}
