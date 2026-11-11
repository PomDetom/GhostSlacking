using System.Drawing;
using System.Runtime.InteropServices;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

public sealed partial class Win32WindowApi
{
    public OperationResult<WindowEnvironment> ReadEnvironment(WindowSnapshot original)
    {
        var hwnd = original.Hwnd;
        var observation = Observe(hwnd);
        if (observation is null || !IsSameIdentity(original, observation))
            return OperationResult<WindowEnvironment>.Failed("Target identity changed while reading display geometry.");

        var monitor = Win32NativeMethods.MonitorFromWindow(hwnd, Win32NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new Win32NativeMethods.MONITORINFOEX
        {
            Size = (uint)Marshal.SizeOf<Win32NativeMethods.MONITORINFOEX>(), DeviceName = string.Empty
        };
        var placement = new Win32NativeMethods.WINDOWPLACEMENT
        {
            Length = (uint)Marshal.SizeOf<Win32NativeMethods.WINDOWPLACEMENT>()
        };
        var dpi = Win32NativeMethods.GetDpiForWindow(hwnd);
        if (monitor == 0 || !Win32NativeMethods.GetMonitorInfo(monitor, ref info) || dpi == 0 ||
            Win32NativeMethods.GetScaleFactorForMonitor(monitor, out var scale) != 0 ||
            !Win32NativeMethods.GetWindowPlacement(hwnd, ref placement))
            return OperationResult<WindowEnvironment>.Failed("Could not read the target display and placement.", Win32NativeMethods.LastError);

        var display = new WindowDisplayContext(info.DeviceName, ToRectangle(info.Monitor), ToRectangle(info.WorkArea), dpi, scale);
        var currentStyle = Win32NativeMethods.GetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE);
        var normalized = WindowPlacementCoordinates.Convert(
            ToSnapshot(placement, observation.ScreenBounds),
            (currentStyle & Win32NativeMethods.WS_EX_TOOLWINDOW) != 0,
            (original.Styles.ExtendedStyle & Win32NativeMethods.WS_EX_TOOLWINDOW) != 0, display);
        // Observe again so a reused HWND is never accepted as a geometry update.
        var after = Observe(hwnd);
        if (after is null || !IsSameIdentity(original, after) || after.ScreenBounds != observation.ScreenBounds)
            return OperationResult<WindowEnvironment>.Failed("Target geometry changed during the display query.");
        if (!after.IsMaximized)
            return OperationResult<WindowEnvironment>.Ok(new(display, observation.ScreenBounds, normalized));
        var expectedRect = new Win32NativeMethods.RECT
        {
            Left = original.ScreenBounds.Left, Top = original.ScreenBounds.Top,
            Right = original.ScreenBounds.Right, Bottom = original.ScreenBounds.Bottom
        };
        var expectedMonitor = Win32NativeMethods.MonitorFromRect(ref expectedRect, Win32NativeMethods.MONITOR_DEFAULTTONEAREST);
        var monitorDpi = (uint)Math.Max(96, scale * 96 / 100);
        int Frame(int metric, uint frameDpi) => Win32NativeMethods.GetSystemMetricsForDpi(metric, frameDpi) +
            Win32NativeMethods.GetSystemMetricsForDpi(92 /* SM_CXPADDEDBORDER */, frameDpi);
        var frameX = Math.Max(Frame(32 /* SM_CXSIZEFRAME */, dpi), Frame(32, monitorDpi));
        var frameY = Math.Max(Frame(33 /* SM_CYSIZEFRAME */, dpi), Frame(33, monitorDpi));
        var isToolWindow = (original.Styles.ExtendedStyle & Win32NativeMethods.WS_EX_TOOLWINDOW) != 0;
        var fitsWorkArea = WindowPlacementRestoration.IsSystemMaximizedBounds(after.ScreenBounds, display.WorkArea, frameX, frameY);
        var fitsToolMonitor = isToolWindow &&
            WindowPlacementRestoration.IsSystemMaximizedBounds(after.ScreenBounds, display.MonitorBounds, frameX, frameY);
        return OperationResult<WindowEnvironment>.Ok(new(display, observation.ScreenBounds, normalized)
        {
            IsSystemMaximized = after.IsMaximized && expectedMonitor == monitor && (fitsWorkArea || fitsToolMonitor)
        });
    }
}

internal static class WindowPlacementCoordinates
{
    internal static WindowPlacementSnapshot Convert(
        WindowPlacementSnapshot placement, bool sourceToolWindow, bool destinationToolWindow, WindowDisplayContext display)
    {
        if (sourceToolWindow == destinationToolWindow) return placement;
        var direction = sourceToolWindow ? -1 : 1;
        var dx = direction * (display.WorkArea.Left - display.MonitorBounds.Left);
        var dy = direction * (display.WorkArea.Top - display.MonitorBounds.Top);
        var normal = placement.NormalPosition;
        normal.Offset(dx, dy);
        Point Translate(Point point) => point == new Point(-1, -1) ? point : new(point.X + dx, point.Y + dy);
        return placement with
        {
            NormalPosition = normal,
            MinPosition = Translate(placement.MinPosition),
            MaxPosition = Translate(placement.MaxPosition)
        };
    }
}
