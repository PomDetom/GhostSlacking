using System.Collections.Concurrent;
using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

internal sealed class Win32BackgroundInputBackend(IWindowApi windows, ILogger logger,
    IExclusiveInputBridge? bridge = null) : IBackgroundInteractionBackend, IDisposable
{
    private readonly IExclusiveInputBridge _bridge = bridge ?? new ExclusiveInputBridge();
    private Point _cursor;
    private readonly byte[] _keyboard = new byte[256];
    private readonly ConcurrentDictionary<nint, (uint Process, uint Thread, string Class)> _recipients = [];
    private readonly HashSet<nint> _menuRecipients = [];
    private nint _clicked;
    private uint _mouseFlags;
    private nint _protectedForeground;
    private WindowSnapshot? _diagnosticSnapshot;
    private (nint Recipient, BackgroundInputCategory Category, BackgroundInputRejectionReason? Rejection)? _lastTrace;

    public nint ForegroundWindow => Win32BackgroundInputNative.GetForegroundWindow();
    internal nint[] MenuWindows => _bridge.Menus();
    internal NativeResult PresentMenus(IReadOnlyList<nint> menus) => _bridge.PresentMenus(menus);
    internal bool IsLiveMenu(nint hwnd) => _bridge.IsMenu(hwnd);
    public bool ContainsInputPoint(WindowSnapshot snapshot, Rectangle targetBounds, Point cursor) =>
        targetBounds.Contains(cursor) || (IsCurrent(snapshot) && _bridge.MenuAt(cursor) != 0);

    internal bool IsCurrent(WindowSnapshot snapshot)
    {
        var observed = windows.Observe(snapshot.Hwnd);
        return observed is not null && windows.IsSameIdentity(snapshot, observed);
    }

    public NativeResult SetNoActivate(WindowSnapshot snapshot, bool restore)
    {
        if (restore) _bridge.Dispose();
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
            _menuRecipients.Clear();
            _lastTrace = null;
            _clicked = 0;
            _mouseFlags = 0;
            _protectedForeground = ForegroundWindow;
            _diagnosticSnapshot = snapshot;
            TraceInputState(snapshot, "prepare");
            _cursor = windows.GetCursorPosition();
            RememberRecipient(hwnd);
            var started = _bridge.Start(snapshot, _protectedForeground);
            logger.Log(started.Success ? LogLevel.Info : LogLevel.Warning,
                $"ExclusiveInputBridge hwnd={hwnd} success={started.Success} operation={started.Operation} error={started.ErrorCode}");
            return started;
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
        var recipient = _bridge.MenuAt(cursor) is { } menu && menu != 0 ? menu : snapshot.Hwnd;
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
            if (!BelongsToInputTarget(snapshot, child)) return 0;
            recipient = child;
        }
        return 0;
    }

    internal bool HasUnmanagedPopup(WindowSnapshot snapshot)
    {
        var popup = Win32BackgroundInputNative.GetLastActivePopup(snapshot.Hwnd);
        if (popup != 0 && popup != snapshot.Hwnd && Win32NativeMethods.IsWindowVisible(popup) && !_bridge.IsMenu(popup)) return true;
        if (!Win32BackgroundInputNative.IsWindowEnabled(snapshot.Hwnd)) return true;
        var thread = Win32NativeMethods.GetWindowThreadProcessId(snapshot.Hwnd, out _);
        var info = new Win32BackgroundInputNative.GuiThreadInfo
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32BackgroundInputNative.GuiThreadInfo>()
        };
        return Win32BackgroundInputNative.GetGUIThreadInfo(thread, ref info) && (info.Flags & 0x1C) != 0 && !_bridge.HasMenus;
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
        if (ValidRecipient(snapshot, _clicked))
        {
            return _clicked;
        }
        var thread = Win32NativeMethods.GetWindowThreadProcessId(snapshot.Hwnd, out _);
        var info = new Win32BackgroundInputNative.GuiThreadInfo
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32BackgroundInputNative.GuiThreadInfo>()
        };
        if (Win32BackgroundInputNative.GetGUIThreadInfo(thread, ref info) && BelongsToTarget(snapshot, info.Focus))
        {
            RememberRecipient(info.Focus);
            return info.Focus;
        }
        var hit = ResolveMouseTarget(snapshot, cursor);
        return ValidRecipient(snapshot, hit) ? hit : 0;
    }

    public NativeResult ReleaseKey(WindowSnapshot snapshot, nint recipient, BackgroundKeyInput input)
    {
        UpdateKeyboardState(input);
        return DispatchKey(snapshot, recipient, input).Result;
    }

    public void UpdateKeyboardState(BackgroundKeyInput input)
    {
        var key = input.VirtualKey;
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

    public BackgroundInputDispatchResult DispatchKey(WindowSnapshot snapshot, nint recipient, BackgroundKeyInput input)
    {
        var valid = GuardRecipient(snapshot, recipient, input.IsDown);
        return Trace(recipient, valid.Success ? DispatchBridgeKey(recipient, input) : new(valid, BackgroundInputCategory.RawKey));
    }

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

    public BackgroundInputDispatchResult DispatchMouse(WindowSnapshot snapshot, nint recipient, BackgroundMouseInput input, Point cursor) =>
        Trace(recipient, DispatchMouseCore(snapshot, recipient, input, cursor));

    private BackgroundInputDispatchResult DispatchMouseCore(WindowSnapshot snapshot, nint recipient, BackgroundMouseInput input, Point cursor)
    {
        if (input.Action == BackgroundMouseAction.Up && input.Button is { } released &&
            _menuRecipients.Contains(recipient) && IsCurrent(snapshot) && !ValidRecipient(snapshot, recipient))
        {
            _mouseFlags &= ~MouseButtonMask(released);
            var closedMessage = released switch
            {
                BackgroundMouseButton.Left => 0x0202U, BackgroundMouseButton.Right => 0x0205U,
                BackgroundMouseButton.Middle => 0x0208U, _ => 0x020CU
            };
            var closedFlags = _mouseFlags | (released == BackgroundMouseButton.X1 ? 1U << 16 : released == BackgroundMouseButton.X2 ? 2U << 16 : 0);
            return new(_bridge.Send(recipient, closedMessage, (nint)closedFlags, 0, _keyboard, cursor), BackgroundInputCategory.Mouse);
        }
        var valid = GuardRecipient(snapshot, recipient, input.Action != BackgroundMouseAction.Up);
        if (!valid.Success) return new(valid, BackgroundInputCategory.Mouse);
        if (input.Action != BackgroundMouseAction.Up && HasUnmanagedPopup(snapshot)) return new(NativeResult.Failed("UnsupportedBackgroundInput", 0, "An unmanaged popup is outside the input scope."), BackgroundInputCategory.Mouse);
        var buttonMask = input.Button is { } button ? MouseButtonMask(button) : 0;
        if (input.Action == BackgroundMouseAction.Down) _mouseFlags |= buttonMask;
        if (input.Action == BackgroundMouseAction.Up) _mouseFlags &= ~buttonMask;
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
        _cursor = cursor;
        return new(_bridge.Send(recipient, message, (nint)flags, parameter, _keyboard, cursor), BackgroundInputCategory.Mouse);
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
        // Native teardown cancels menus before releasing delivered downs.
        // An ordinary queued up here could execute a pressed menu item.
        _mouseFlags &= ~MouseButtonMask(button);
        return NativeResult.Ok("ExclusiveInputBridgeDeferredRelease");
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

    private bool BelongsToInputTarget(WindowSnapshot snapshot, nint recipient) =>
        BelongsToTarget(snapshot, recipient) || _bridge.IsMenu(recipient);

    private void RememberRecipient(nint recipient)
    {
        var thread = Win32NativeMethods.GetWindowThreadProcessId(recipient, out var process);
        var name = ClassName(recipient);
        _recipients[recipient] = (process, thread, name);
        _menuRecipients.Remove(recipient);
        if (_bridge.IsMenu(recipient)) _menuRecipients.Add(recipient);
    }

    private bool ValidRecipient(WindowSnapshot snapshot, nint recipient)
    {
        if (!BelongsToInputTarget(snapshot, recipient) || !_recipients.TryGetValue(recipient, out var identity)) return false;
        var thread = Win32NativeMethods.GetWindowThreadProcessId(recipient, out var process);
        return thread == identity.Thread && process == identity.Process && ClassName(recipient) == identity.Class;
    }

    private static string ClassName(nint hwnd)
    {
        var buffer = new char[256];
        var count = Win32BackgroundInputNative.GetClassName(hwnd, buffer, buffer.Length);
        return count > 0 ? new string(buffer, 0, count) : string.Empty;
    }

    internal NativeResult MaintainBridge() => _bridge.Renew();

    public void ResetInputState()
    {
        _bridge.Dispose();
        _menuRecipients.Clear();
        if (_diagnosticSnapshot is { } snapshot) TraceInputState(snapshot, "reset");
        _diagnosticSnapshot = null;
        Array.Clear(_keyboard);
        _mouseFlags = 0;
        _clicked = 0;
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
            $"targetFocus={(observed ? info.Focus : 0)} layout={Win32BackgroundInputNative.GetKeyboardLayout(thread)} imeWindow={ime} imeQueryAvailable={known} imeOpen={open} engine=US-English");
    }

    public void Dispose() => _bridge.Dispose();

    private BackgroundInputDispatchResult DispatchBridgeKey(nint recipient, BackgroundKeyInput input)
    {
        var key = input.VirtualKey;
        if (key is < 1 or > 255) return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.UnsupportedKey);
        if (key is 0xE5 or 0xE7 or 0x15 or 0x19 or 0x1C or 0x1D ||
            key == 0x20 && (Modifiers.HasFlag(ShortcutModifiers.Control) || (_keyboard[0x5B] & 0x80) != 0 || (_keyboard[0x5C] & 0x80) != 0))
            return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.ImeComposition);
        if (key is 0x5B or 0x5C || Modifiers.HasFlag(ShortcutModifiers.Alt))
            return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.SystemShortcut);
        var raw = _bridge.Send(recipient, input.IsDown ? 0x0100U : 0x0101U,
            MessageVirtualKey(key), KeyMessageParameter(input, input.IsRepeat, false), _keyboard, _cursor);
        if (!raw.Success) return new(raw, BackgroundInputCategory.RawKey, input.IsDown);
        // Character translation now happens only if the target message loop
        // calls TranslateMessage. Accelerator-consumed keys produce no WM_CHAR.
        return new(raw, BackgroundInputCategory.RawKey, input.IsDown);
    }

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
            $"BackgroundInputDispatch hwnd={recipient} class={name} bridge=ExclusiveInputBridge " +
            $"targetFocus={info.Focus} category={result.Category} rejection={result.Rejection?.ToString() ?? "None"} " +
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

    private NativeResult DeliveryFailure(string operation)
    {
        var error = Win32NativeMethods.LastError;
        logger.Log(LogLevel.Warning, $"BackgroundInteraction delivery failed operation={operation} error={error}");
        return NativeResult.Failed(operation, error, "Background input could not be posted.");
    }
}
