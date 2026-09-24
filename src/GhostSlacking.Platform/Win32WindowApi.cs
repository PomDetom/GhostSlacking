using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

public sealed class Win32WindowApi : IWindowApi
{
    public bool IsWindow(nint hwnd) => hwnd != 0 && Win32NativeMethods.IsWindow(hwnd);

    public WindowObservation? Observe(nint hwnd)
    {
        if (!IsWindow(hwnd) || !Win32NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        Win32NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return new WindowObservation
        {
            Hwnd = hwnd,
            ProcessId = pid,
            ScreenBounds = ToRectangle(rect),
            IsVisible = Win32NativeMethods.IsWindowVisible(hwnd),
            IsMinimized = Win32NativeMethods.IsIconic(hwnd),
            IsMaximized = Win32NativeMethods.IsZoomed(hwnd),
            // The coordinator does not consume the process name while polling.
            // Avoid opening a process handle on every Reveal frame.
            ProcessName = null
        };
    }

    public OperationResult<WindowSnapshot> CaptureSnapshot(TargetWindow target)
    {
        if (!IsWindow(target.Hwnd) || !Win32NativeMethods.GetWindowRect(target.Hwnd, out var rect))
        {
            return OperationResult<WindowSnapshot>.Failed("Target window disappeared while taking its snapshot.", Win32NativeMethods.LastError);
        }

        Win32NativeMethods.GetWindowThreadProcessId(target.Hwnd, out var processId);
        if (processId == 0 || processId != target.ProcessId)
        {
            return OperationResult<WindowSnapshot>.Failed("Target process identity changed before snapshot.");
        }

        var region = CaptureRegion(target.Hwnd, out var regionError);
        if (regionError is not null)
        {
            return OperationResult<WindowSnapshot>.Failed(regionError, Win32NativeMethods.LastError);
        }

        var processStartIdentity = TryGetProcessStartIdentity(processId);
        if (processStartIdentity is null)
        {
            return OperationResult<WindowSnapshot>.Failed(
                "Could not capture the target process start identity. The window was not modified.");
        }

        var placement = new Win32NativeMethods.WINDOWPLACEMENT
        {
            Length = (uint)Marshal.SizeOf<Win32NativeMethods.WINDOWPLACEMENT>()
        };
        if (!Win32NativeMethods.GetWindowPlacement(target.Hwnd, ref placement))
        {
            return OperationResult<WindowSnapshot>.Failed(
                "Could not capture the target window placement. The window was not modified.",
                Win32NativeMethods.LastError);
        }

        return OperationResult<WindowSnapshot>.Ok(new WindowSnapshot
        {
            Hwnd = target.Hwnd,
            ProcessId = processId,
            ProcessName = TryGetProcessName(processId) ?? target.ProcessName,
            ProcessStartIdentity = processStartIdentity,
            ScreenBounds = ToRectangle(rect),
            WasVisible = Win32NativeMethods.IsWindowVisible(target.Hwnd),
            WasMinimized = Win32NativeMethods.IsIconic(target.Hwnd),
            Placement = ToSnapshot(placement, ToRectangle(rect)),
            OriginalRegionData = region,
            OriginalSystemBackdropType = TryGetSystemBackdropType(target.Hwnd),
            OriginalNonClientRenderingPolicy = TryGetNonClientRenderingPolicy(target.Hwnd),
            OriginalWindowCornerPreference = TryGetDwmAttribute(target.Hwnd, Win32NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE),
            OriginalBorderColor = TryGetDwmAttribute(target.Hwnd, Win32NativeMethods.DWMWA_BORDER_COLOR),
            Styles = new WindowStyleSnapshot(
                Win32NativeMethods.GetWindowLongPtr(target.Hwnd, Win32NativeMethods.GWL_STYLE),
                Win32NativeMethods.GetWindowLongPtr(target.Hwnd, Win32NativeMethods.GWL_EXSTYLE)),
            CapturedAt = DateTimeOffset.UtcNow
        });
    }

    public bool IsSameIdentity(WindowSnapshot snapshot, WindowObservation observation)
    {
        if (snapshot.Hwnd != observation.Hwnd || snapshot.ProcessId != observation.ProcessId)
        {
            return false;
        }

        var currentStart = TryGetProcessStartIdentity(observation.ProcessId);
        return currentStart is not null && snapshot.ProcessStartIdentity == currentStart;
    }

    public Point GetCursorPosition()
    {
        return Win32NativeMethods.GetCursorPos(out var point) ? new Point(point.X, point.Y) : Point.Empty;
    }

    public bool IsKeyDown(int virtualKey) => (Win32NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public NativeResult BringToForeground(nint hwnd)
    {
        if (!IsWindow(hwnd))
        {
            return NativeResult.Failed("SetForegroundWindow", 0, "The window is unavailable.");
        }

        if (Win32NativeMethods.IsIconic(hwnd))
        {
            Win32NativeMethods.ShowWindow(hwnd, Win32NativeMethods.SW_RESTORE);
        }

        return Win32NativeMethods.SetForegroundWindow(hwnd)
            ? NativeResult.Ok("SetForegroundWindow")
            : NativeResult.Failed(
                "SetForegroundWindow",
                Win32NativeMethods.LastError,
                "Windows did not allow the window to move to the foreground.");
    }

    public NativeResult BringToTopWithoutActivation(nint hwnd)
    {
        if (!IsWindow(hwnd))
        {
            return NativeResult.Failed("SetWindowPos(NotificationTopmost)", 0, "The window is unavailable.");
        }

        var flags = Win32NativeMethods.SWP_NOMOVE |
            Win32NativeMethods.SWP_NOSIZE |
            Win32NativeMethods.SWP_NOACTIVATE |
            Win32NativeMethods.SWP_SHOWWINDOW;
        return Win32NativeMethods.SetWindowPos(
            hwnd,
            Win32NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            flags)
            ? NativeResult.Ok("SetWindowPos(NotificationTopmost)")
            : NativeResult.Failed(
                "SetWindowPos(NotificationTopmost)",
                Win32NativeMethods.LastError,
                "Could not raise the notification window without activation.");
    }

    public NativeResult SetNotificationClickThrough(nint hwnd, bool enabled)
    {
        if (!IsWindow(hwnd))
        {
            return NativeResult.Failed("SetWindowLongPtr(NotificationClickThrough)", 0, "The window is unavailable.");
        }

        var style = Win32NativeMethods.GetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE);
        var updated = enabled
            ? style | (nint)Win32NativeMethods.WS_EX_TRANSPARENT
            : style & ~(nint)Win32NativeMethods.WS_EX_TRANSPARENT;
        if (updated == style)
        {
            return NativeResult.Ok("SetWindowLongPtr(NotificationClickThrough)");
        }

        Win32NativeMethods.SetLastError(0);
        Win32NativeMethods.SetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE, updated);
        var error = Win32NativeMethods.LastError;
        if (error != 0)
        {
            return NativeResult.Failed("SetWindowLongPtr(NotificationClickThrough)", error, "Could not update notification hit testing.");
        }

        var flags = Win32NativeMethods.SWP_NOMOVE |
            Win32NativeMethods.SWP_NOSIZE |
            Win32NativeMethods.SWP_NOZORDER |
            Win32NativeMethods.SWP_NOACTIVATE |
            Win32NativeMethods.SWP_FRAMECHANGED;
        return Win32NativeMethods.SetWindowPos(hwnd, 0, 0, 0, 0, 0, flags)
            ? NativeResult.Ok("SetWindowPos(NotificationClickThrough)")
            : NativeResult.Failed("SetWindowPos(NotificationClickThrough)", Win32NativeMethods.LastError, "Could not apply notification hit testing.");
    }

    private static byte[]? CaptureRegion(nint hwnd, out string? error)
    {
        error = null;
        var region = Win32NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (region == 0)
        {
            error = "CreateRectRgn failed while capturing the original region.";
            return null;
        }

        try
        {
            var result = Win32NativeMethods.GetWindowRgn(hwnd, region);
            if (result == 0)
            {
                return null;
            }

            var size = Win32NativeMethods.GetRegionData(region, 0, 0);
            if (size == 0)
            {
                error = "GetRegionData failed while capturing the original region.";
                return null;
            }

            var data = new byte[size];
            var handle = Marshal.AllocHGlobal((int)size);
            try
            {
                if (Win32NativeMethods.GetRegionData(region, (uint)size, handle) == 0)
                {
                    error = "GetRegionData failed while copying the original region.";
                    return null;
                }

                Marshal.Copy(handle, data, 0, data.Length);
                return data;
            }
            finally
            {
                Marshal.FreeHGlobal(handle);
            }
        }
        finally
        {
            Win32NativeMethods.DeleteObject(region);
        }
    }

    private static Rectangle ToRectangle(Win32NativeMethods.RECT rect) =>
        Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);

    private static WindowPlacementSnapshot ToSnapshot(
        Win32NativeMethods.WINDOWPLACEMENT placement,
        Rectangle deviceBounds) => new(
        (int)placement.Flags,
        (int)placement.ShowCmd,
        new Point(placement.MinPosition.X, placement.MinPosition.Y),
        new Point(placement.MaxPosition.X, placement.MaxPosition.Y),
        ToRectangle(placement.NormalPosition),
        deviceBounds);

    private static string? TryGetProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? TryGetProcessStartIdentity(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    private static int? TryGetSystemBackdropType(nint hwnd)
    {
        return Win32NativeMethods.DwmGetWindowAttribute(
            hwnd, Win32NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, out var value, sizeof(int)) == 0 ? value : null;
    }

    private static int? TryGetNonClientRenderingPolicy(nint hwnd)
    {
        return Win32NativeMethods.DwmGetWindowAttribute(
            hwnd, Win32NativeMethods.DWMWA_NCRENDERING_POLICY, out var value, sizeof(int)) == 0 ? value : null;
    }

    private static int? TryGetDwmAttribute(nint hwnd, uint attribute)
    {
        return Win32NativeMethods.DwmGetWindowAttribute(hwnd, attribute, out var value, sizeof(int)) == 0 ? value : null;
    }
}
