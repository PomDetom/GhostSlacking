using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

internal interface IExclusiveInputBridge : IDisposable
{
    NativeResult Start(WindowSnapshot snapshot, nint foreground);
    NativeResult Renew();
    nint MenuAt(Point cursor);
    bool IsMenu(nint window);
    bool HasMenus { get; }
    nint[] Menus();
    NativeResult PresentMenus(IReadOnlyList<nint> menus);
    NativeResult Send(nint recipient, uint message, nint wparam, nint lparam, byte[] keyboard, Point cursor);
}

// Experimental target-thread input backend. The gate serializes native mapping access
// with its UI-loop lease and disposal; no text or key data is logged.
internal sealed class ExclusiveInputBridge : IExclusiveInputBridge
{
    private readonly object _gate = new();
    private bool _active;

    public NativeResult Start(WindowSnapshot snapshot, nint foreground)
    {
        lock (_gate)
        {
            try
            {
                InputBridgeLibrary.EnsureLoaded();
                if (!long.TryParse(snapshot.ProcessStartIdentity, NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
                    start < DateTime.FromFileTimeUtc(0).Ticks)
                    return NativeResult.Failed("ExclusiveInputBridgeIdentity", 0, "The captured process start identity is invalid.");
                var fileTime = (ulong)(start - DateTime.FromFileTimeUtc(0).Ticks);
                var error = Begin(snapshot.Hwnd, foreground, snapshot.ProcessId, fileTime);
                if (error != 0) return NativeResult.Failed("ExclusiveInputBridgeStart", (int)error, "The x64 target-thread bridge could not be prepared.");
                _active = true;
                return NativeResult.Ok("ExclusiveInputBridgeStart");
            }
            catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or IOException or UnauthorizedAccessException)
            {
                return NativeResult.Failed("ExclusiveInputBridgeUnavailable", error.HResult, "The installed input bridge could not be loaded. Repair the installation before trying again.");
            }
        }
    }

    public NativeResult Renew()
    {
        lock (_gate)
        {
            if (!_active) return NativeResult.Ok("ExclusiveInputBridgeIdle");
            var error = Heartbeat();
            return error == 0 ? NativeResult.Ok("ExclusiveInputBridgeLease")
                : Failure("ExclusiveInputBridgeLease", error);
        }
    }

    public nint MenuAt(Point cursor)
    {
        lock (_gate) return _active ? FindMenu(cursor.X, cursor.Y) : 0;
    }

    public bool IsMenu(nint window)
    {
        lock (_gate) return _active && CheckMenu(window);
    }
    public nint[] Menus()
    {
        lock (_gate)
        {
            if (!_active) return [];
            var windows = new nint[64];
            var count = GetMenus(windows, (uint)windows.Length);
            if (count > windows.Length) throw new InvalidOperationException("Too many popup windows in one input session.");
            return windows[..(int)count];
        }
    }

    public bool HasMenus
    {
        get { lock (_gate) return _active && AnyMenus(); }
    }
    public NativeResult PresentMenus(IReadOnlyList<nint> menus)
    {
        lock (_gate)
        {
            var error = _active ? Present(menus.ToArray(), (uint)menus.Count) : 5023U;
            return error == 0 ? NativeResult.Ok("MenuRevealPresent") : NativeResult.Failed("MenuRevealPresent", (int)error);
        }
    }

    public NativeResult Send(nint recipient, uint message, nint wparam, nint lparam, byte[] keyboard, Point cursor)
    {
        lock (_gate)
        {
            if (!_active) return NativeResult.Failed("ExclusiveInputBridgeInactive", 0, "The target-thread bridge is not active.");
            var error = Dispatch(recipient, message, wparam, lparam, keyboard, cursor.X, cursor.Y);
            return error == 0 ? NativeResult.Ok("ExclusiveInputBridgeDispatch")
                : Failure("ExclusiveInputBridgeDispatch", error);
        }
    }

    private static NativeResult Failure(string operation, uint error) => NativeResult.Failed(ErrorStage() switch
    {
        1 => "MenuRevealCaptureRegion",
        2 => "MenuRevealShow",
        3 => "MenuRevealReapplyRegion",
        _ => operation
    }, (int)error, "The operation failed or its result is uncertain; it must not be retried.");

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_active) return;
            _active = false;
            End();
        }
    }

    [DllImport(InputBridgeLibrary.LibraryName, EntryPoint = "GS_Begin", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint Begin(nint root, nint foreground, uint expectedPid, ulong expectedStart);
    [DllImport(InputBridgeLibrary.LibraryName, EntryPoint = "GS_Dispatch", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint Dispatch(nint recipient, uint message, nint wparam, nint lparam, [In] byte[] keyboard, int x, int y);
    [DllImport(InputBridgeLibrary.LibraryName, EntryPoint = "GS_Heartbeat", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint Heartbeat();
    [DllImport(InputBridgeLibrary.LibraryName, EntryPoint = "GS_End", CallingConvention = CallingConvention.Cdecl)]
    private static extern void End();
    [DllImport(InputBridgeLibrary.LibraryName, EntryPoint = "GS_MenuAt", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint FindMenu(int x, int y);
    [DllImport(InputBridgeLibrary.LibraryName, EntryPoint = "GS_IsMenu", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CheckMenu(nint window);
    [DllImport(InputBridgeLibrary.LibraryName, EntryPoint = "GS_HasMenus", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AnyMenus();
    [DllImport(InputBridgeLibrary.LibraryName, EntryPoint = "GS_GetMenus", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint GetMenus([Out] nint[] windows, uint capacity);
    [DllImport(InputBridgeLibrary.LibraryName, EntryPoint = "GS_PresentMenus", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint Present([In] nint[] menus, uint count);
    [DllImport(InputBridgeLibrary.LibraryName, EntryPoint = "GS_ErrorStage", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint ErrorStage();
}
