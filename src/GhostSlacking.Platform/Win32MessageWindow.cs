using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GhostSlacking.Platform;

public sealed class Win32MessageWindow : IDisposable
{
    private const string WindowClassName = "GhostSlacking.MessageWindow";
    private static readonly object ClassGate = new();
    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, Win32MessageWindow> Instances = [];
    private static readonly Win32NativeMethods.WindowProc WindowProcedure = WindowProc;
    private static bool _classRegistered;
    private bool _disposed;

    public Win32MessageWindow()
    {
        EnsureWindowClass();
        Handle = Win32NativeMethods.CreateWindowEx(
            0,
            WindowClassName,
            WindowClassName,
            0,
            0,
            0,
            0,
            0,
            Win32NativeMethods.HWND_MESSAGE,
            0,
            Win32NativeMethods.GetModuleHandle(null),
            0);
        if (Handle == 0)
        {
            throw new Win32Exception(Win32NativeMethods.LastError, "Could not create the hotkey message window.");
        }

        lock (InstancesGate)
        {
            Instances.Add(Handle, this);
        }
    }

    public nint Handle { get; private set; }

    public event Action<int>? HotkeyPressed;

    private static void EnsureWindowClass()
    {
        lock (ClassGate)
        {
            if (_classRegistered)
            {
                return;
            }

            var windowClass = new Win32NativeMethods.WNDCLASSEX
            {
                Size = (uint)Marshal.SizeOf<Win32NativeMethods.WNDCLASSEX>(),
                WindowProcedure = WindowProcedure,
                Instance = Win32NativeMethods.GetModuleHandle(null),
                ClassName = WindowClassName
            };
            if (Win32NativeMethods.RegisterClassEx(ref windowClass) == 0 &&
                Win32NativeMethods.LastError != Win32NativeMethods.ERROR_CLASS_ALREADY_EXISTS)
            {
                throw new Win32Exception(Win32NativeMethods.LastError, "Could not register the hotkey message window class.");
            }

            _classRegistered = true;
        }
    }

    private static nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == Win32NativeMethods.WM_HOTKEY)
        {
            Win32MessageWindow? instance;
            lock (InstancesGate)
            {
                Instances.TryGetValue(hwnd, out instance);
            }

            instance?.HotkeyPressed?.Invoke(wParam.ToInt32());
        }

        return Win32NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var handle = Handle;
        Handle = 0;
        if (handle != 0)
        {
            lock (InstancesGate)
            {
                Instances.Remove(handle);
            }

            Win32NativeMethods.DestroyWindow(handle);
        }

        GC.SuppressFinalize(this);
    }
}
