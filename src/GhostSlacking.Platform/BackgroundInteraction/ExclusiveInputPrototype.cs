using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

internal interface IExclusiveInputPrototype : IDisposable
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

// Explicit opt-in research backend. The gate serializes native mapping access
// with its UI-loop lease and disposal; no text or key data is logged.
internal sealed class ExclusiveInputPrototype : IExclusiveInputPrototype
{
    private readonly object _gate = new();
    private bool _active;

    public NativeResult Start(WindowSnapshot snapshot, nint foreground)
    {
        lock (_gate)
        {
            try
            {
                if (!long.TryParse(snapshot.ProcessStartIdentity, NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
                    start < DateTime.FromFileTimeUtc(0).Ticks)
                    return NativeResult.Failed("ExclusiveInputPrototypeIdentity", 0, "The captured process start identity is invalid.");
                var fileTime = (ulong)(start - DateTime.FromFileTimeUtc(0).Ticks);
                var error = Begin(snapshot.Hwnd, foreground, snapshot.ProcessId, fileTime);
                if (error != 0) return NativeResult.Failed("ExclusiveInputPrototypeStart", (int)error, "The x64 target-thread bridge could not be prepared.");
                _active = true;
                return NativeResult.Ok("ExclusiveInputPrototypeStart");
            }
            catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
            {
                return NativeResult.Failed("ExclusiveInputPrototypeMissing", 0, "Build the exclusive input prototype before using its launch switch.");
            }
        }
    }

    public NativeResult Renew()
    {
        lock (_gate)
        {
            if (!_active) return NativeResult.Ok("ExclusiveInputPrototypeIdle");
            var error = Heartbeat();
            return error == 0 ? NativeResult.Ok("ExclusiveInputPrototypeLease")
                : Failure("ExclusiveInputPrototypeLease", error);
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
            if (!_active) return NativeResult.Failed("ExclusiveInputPrototypeInactive", 0, "The target-thread bridge is not active.");
            var error = Dispatch(recipient, message, wparam, lparam, keyboard, cursor.X, cursor.Y);
            return error == 0 ? NativeResult.Ok("ExclusiveInputPrototypeDispatch")
                : Failure("ExclusiveInputPrototypeDispatch", error);
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

    [DllImport("GhostSlacking.InputPrototype.MenuHover.dll", EntryPoint = "GS_Begin", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint Begin(nint root, nint foreground, uint expectedPid, ulong expectedStart);
    [DllImport("GhostSlacking.InputPrototype.MenuHover.dll", EntryPoint = "GS_Dispatch", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint Dispatch(nint recipient, uint message, nint wparam, nint lparam, [In] byte[] keyboard, int x, int y);
    [DllImport("GhostSlacking.InputPrototype.MenuHover.dll", EntryPoint = "GS_Heartbeat", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint Heartbeat();
    [DllImport("GhostSlacking.InputPrototype.MenuHover.dll", EntryPoint = "GS_End", CallingConvention = CallingConvention.Cdecl)]
    private static extern void End();
    [DllImport("GhostSlacking.InputPrototype.MenuHover.dll", EntryPoint = "GS_MenuAt", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint FindMenu(int x, int y);
    [DllImport("GhostSlacking.InputPrototype.MenuHover.dll", EntryPoint = "GS_IsMenu", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CheckMenu(nint window);
    [DllImport("GhostSlacking.InputPrototype.MenuHover.dll", EntryPoint = "GS_HasMenus", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AnyMenus();
    [DllImport("GhostSlacking.InputPrototype.MenuHover.dll", EntryPoint = "GS_GetMenus", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint GetMenus([Out] nint[] windows, uint capacity);
    [DllImport("GhostSlacking.InputPrototype.MenuHover.dll", EntryPoint = "GS_PresentMenus", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint Present([In] nint[] menus, uint count);
    [DllImport("GhostSlacking.InputPrototype.MenuHover.dll", EntryPoint = "GS_ErrorStage", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint ErrorStage();
}
