using System.Text;
using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

public sealed class Win32WindowPicker
{
    private readonly uint _ownProcessId;
    private readonly Func<IEnumerable<nint>> _excludedHandles;

    public Win32WindowPicker(uint ownProcessId, Func<IEnumerable<nint>>? excludedHandles = null)
    {
        _ownProcessId = ownProcessId;
        _excludedHandles = excludedHandles ?? (() => Array.Empty<nint>());
    }

    public TargetWindow? PickAt(Point screenPoint)
    {
        var point = new Win32NativeMethods.POINT { X = screenPoint.X, Y = screenPoint.Y };
        var hwnd = Win32NativeMethods.WindowFromPoint(point);
        if (hwnd == 0)
        {
            return null;
        }

        hwnd = Win32NativeMethods.GetAncestor(hwnd, Win32NativeMethods.GA_ROOT);
        if (!IsSelectable(hwnd) || !Win32NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        Win32NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || !Win32NativeMethods.GetWindowRect(hwnd, out rect))
        {
            return null;
        }

        return new TargetWindow
        {
            Hwnd = hwnd,
            ProcessId = pid,
            Title = ReadText(hwnd),
            ProcessName = TryGetProcessName(pid),
            ScreenBounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom)
        };
    }

    private bool IsSelectable(nint hwnd)
    {
        if (hwnd == 0 || !Win32NativeMethods.IsWindow(hwnd) || !Win32NativeMethods.IsWindowVisible(hwnd))
        {
            return false;
        }

        if (_excludedHandles().Contains(hwnd) || hwnd == Win32NativeMethods.GetShellWindow())
        {
            return false;
        }

        Win32NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == _ownProcessId)
        {
            return false;
        }

        var className = ReadClassName(hwnd);
        return className is not "Progman" and not "WorkerW" and not "Shell_TrayWnd" and not "DV2ControlHost";
    }

    private static string? ReadText(nint hwnd)
    {
        var buffer = new char[512];
        var length = Win32NativeMethods.GetWindowText(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

    private static string? ReadClassName(nint hwnd)
    {
        var buffer = new char[256];
        var length = Win32NativeMethods.GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

    private static string? TryGetProcessName(uint pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
