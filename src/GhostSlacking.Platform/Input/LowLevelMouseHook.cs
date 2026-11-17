using System.Runtime.InteropServices;
using System.Drawing;

namespace GhostSlacking.Platform;

public sealed class LowLevelMouseHook : IDisposable
{
    private readonly Win32NativeMethods.HookProc _callback;
    private nint _hook;

    public LowLevelMouseHook()
    {
        _callback = Callback;
    }

    public event EventHandler<MouseButtonEventArgs>? LeftButtonDown;

    public bool Start()
    {
        if (_hook != 0)
        {
            return true;
        }

        _hook = Win32NativeMethods.SetWindowsHookEx(
            Win32NativeMethods.WH_MOUSE_LL, _callback, Win32NativeMethods.GetModuleHandle(null), 0);
        return _hook != 0;
    }

    public void Stop()
    {
        if (_hook != 0)
        {
            Win32NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    public void Dispose() => Stop();

    private nint Callback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && wParam == Win32NativeMethods.WM_LBUTTONDOWN)
        {
            var data = Marshal.PtrToStructure<Win32NativeMethods.MSLLHOOKSTRUCT>(lParam);
            LeftButtonDown?.Invoke(this, new MouseButtonEventArgs(new Point(data.Point.X, data.Point.Y)));
        }

        return Win32NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }
}
