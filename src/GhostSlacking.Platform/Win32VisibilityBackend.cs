using System.Runtime.InteropServices;
using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

public sealed class Win32VisibilityBackend : IVisibilityBackend
{
    private readonly ILogger _logger;

    public Win32VisibilityBackend(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    public NativeResult ApplyGhost(nint hwnd)
    {
        if (!Win32NativeMethods.IsWindow(hwnd))
        {
            return Failure("ShowWindow(SW_HIDE)", "The target window no longer exists.");
        }

        var taskSwitchStyle = SetGhostTaskSwitchStyle(hwnd);
        if (!taskSwitchStyle.Success)
        {
            return taskSwitchStyle;
        }

        // Windows 11 can paint a system backdrop behind the non-client/client
        // surface independently of the app's content. Disable it while the
        // window is ghosted so the area outside the region is transparent.
        DisableSystemBackdrop(hwnd);
        DisableNonClientRendering(hwnd);
        DisableWindowDecorations(hwnd);

        var topmost = SetTopmost(hwnd);
        if (!topmost.Success)
        {
            return topmost;
        }

        // ShowWindow returns the previous visibility, not whether the call succeeded.
        Win32NativeMethods.ShowWindow(hwnd, Win32NativeMethods.SW_HIDE);
        return NativeResult.Ok("ShowWindow(SW_HIDE)");
    }

    public NativeResult ApplyReveal(nint hwnd, CircleRegion region)
    {
        var applied = ApplyRevealRegion(hwnd, region);
        if (!applied.Success)
        {
            return applied;
        }

        // ShowWindow returns the previous visibility, so its return value cannot
        // be used as a success flag. The first region is installed while hidden
        // to avoid exposing the full window during the transition.
        Win32NativeMethods.ShowWindow(hwnd, Win32NativeMethods.SW_SHOWNOACTIVATE);

        // Some browser/Electron/self-drawn windows update their frame during
        // WM_WINDOWPOSCHANGED. Reapply after showing so the visible frame is
        // guaranteed to retain the selected clip instead of flashing/showing a
        // full rectangular mask.
        var refreshed = ApplyRevealRegion(hwnd, region);
        if (!refreshed.Success)
        {
            Win32NativeMethods.ShowWindow(hwnd, Win32NativeMethods.SW_HIDE);
            return refreshed;
        }

        // Force DWM and the target's non-client/client areas to discard any
        // rectangular pixels painted during the show transition.
        Win32NativeMethods.RedrawWindow(hwnd, 0, 0,
            Win32NativeMethods.RDW_INVALIDATE | Win32NativeMethods.RDW_ERASE |
            Win32NativeMethods.RDW_UPDATENOW | Win32NativeMethods.RDW_FRAME);

        return NativeResult.Ok("Reveal");
    }

    public NativeResult Restore(nint hwnd, WindowSnapshot snapshot)
    {
        if (snapshot.OriginalRegionData is null)
        {
            if (!Win32NativeMethods.SetWindowRgn(hwnd, 0, true))
            {
                return Failure("SetWindowRgn(RestoreClear)", "Could not clear the Ghost region.");
            }
        }
        else
        {
            var region = CreateRegion(snapshot.OriginalRegionData);
            if (region == 0)
            {
                return Failure("ExtCreateRegion", "Could not recreate the original window region.");
            }

            if (!Win32NativeMethods.SetWindowRgn(hwnd, region, true))
            {
                Win32NativeMethods.DeleteObject(region);
                return Failure("SetWindowRgn(RestoreRegion)", "Could not restore the original window region.");
            }
        }

        var restoredStyle = RestoreOriginalStyle(hwnd, snapshot);
        if (!restoredStyle.Success)
        {
            return restoredStyle;
        }

        RestoreSystemBackdrop(hwnd, snapshot.OriginalSystemBackdropType);
        RestoreNonClientRendering(hwnd, snapshot.OriginalNonClientRenderingPolicy);
        RestoreWindowDecorations(hwnd, snapshot.OriginalWindowCornerPreference, snapshot.OriginalBorderColor);
        RestoreTopmost(hwnd, snapshot.Styles.ExtendedStyle);

        var showCommand = snapshot.WasMinimized
            ? Win32NativeMethods.SW_MINIMIZE
            : snapshot.WasVisible ? Win32NativeMethods.SW_SHOWNOACTIVATE : Win32NativeMethods.SW_HIDE;
        Win32NativeMethods.ShowWindow(hwnd, showCommand);

        return NativeResult.Ok("Restore");
    }

    private NativeResult ApplyOwnedRegion(nint hwnd, nint region, string operation)
    {
        if (!Win32NativeMethods.SetWindowRgn(hwnd, region, true))
        {
            var error = Win32NativeMethods.LastError;
            Win32NativeMethods.DeleteObject(region);
            return Failure(operation, $"SetWindowRgn failed with error {error}.", error);
        }

        // SetWindowRgn takes ownership of a successful region handle.
        return NativeResult.Ok(operation);
    }

    private NativeResult ApplyRevealRegion(nint hwnd, CircleRegion region)
    {
        var bounds = RevealGeometry.GetBounds(region);
        // Elliptic and round-rect regions lose the last raster row/column when
        // their right and bottom coordinates equal the requested bounds, so
        // compensate for both curved shapes. Rectangular GDI regions already
        // retain the exact box.
        var nativeRegion = region.Shape switch
        {
            RevealShape.Circle => Win32NativeMethods.CreateEllipticRgn(bounds.Left, bounds.Top, bounds.Right + 1, bounds.Bottom + 1),
            RevealShape.Rectangle => Win32NativeMethods.CreateRectRgn(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
            RevealShape.RoundedRectangle => Win32NativeMethods.CreateRoundRectRgn(
                bounds.Left,
                bounds.Top,
                bounds.Right + 1,
                bounds.Bottom + 1,
                region.CornerRadius * 2,
                region.CornerRadius * 2),
            _ => 0
        };
        if (nativeRegion == 0)
        {
            return Failure($"Create{region.Shape}Rgn", "Could not create the Reveal region.");
        }

        var applied = ApplyOwnedRegion(hwnd, nativeRegion, "SetWindowRgn(Reveal)");
        if (!applied.Success)
        {
            return applied;
        }

        return ValidateRegion(hwnd, RevealGeometry.GetBounds(region));
    }

    private NativeResult ValidateRegion(nint hwnd, Rectangle expected)
    {
        var probe = Win32NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (probe == 0)
        {
            return Failure("CreateRectRgn(Validate)", "Could not create a region validation handle.");
        }

        try
        {
            var result = Win32NativeMethods.GetWindowRgn(hwnd, probe);
            if (result == Win32NativeMethods.ERRORREGION)
            {
                return Failure("GetWindowRgn(Validate)", "The target window did not retain the Reveal region.");
            }

            if (result == Win32NativeMethods.NULLREGION || Win32NativeMethods.GetRgnBox(probe, out var actual) == Win32NativeMethods.NULLREGION)
            {
                return Failure("GetRgnBox(Validate)", "The target window Reveal region is empty.");
            }

            var actualBounds = Rectangle.FromLTRB(actual.Left, actual.Top, actual.Right, actual.Bottom);
            if (actualBounds != expected)
            {
                return Failure("GetRgnBox(Validate)", $"Reveal region mismatch; expected={expected}, actual={actualBounds}.");
            }

            return NativeResult.Ok("ValidateRevealRegion");
        }
        finally
        {
            Win32NativeMethods.DeleteObject(probe);
        }
    }

    private NativeResult SetGhostTaskSwitchStyle(nint hwnd)
    {
        var currentStyle = Win32NativeMethods.GetWindowLongPtr(hwnd, Win32NativeMethods.GWL_STYLE);
        var frameLessStyle = (nint)((long)currentStyle & ~(
            (long)Win32NativeMethods.WS_CAPTION |
            (long)Win32NativeMethods.WS_DLGFRAME |
            (long)Win32NativeMethods.WS_THICKFRAME |
            (long)Win32NativeMethods.WS_MINIMIZEBOX |
            (long)Win32NativeMethods.WS_MAXIMIZEBOX |
            (long)Win32NativeMethods.WS_SYSMENU));
        Win32NativeMethods.SetLastError(0);
        Win32NativeMethods.SetWindowLongPtr(hwnd, Win32NativeMethods.GWL_STYLE, frameLessStyle);
        var styleError = Win32NativeMethods.LastError;
        if (styleError != 0)
        {
            return Failure("SetWindowLongPtr(GhostFrame)", "Could not remove the target window frame.", styleError);
        }

        var current = Win32NativeMethods.GetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE);
        var updated = (nint)((long)current | (long)Win32NativeMethods.WS_EX_TOOLWINDOW);
        updated = (nint)((long)updated & ~(long)Win32NativeMethods.WS_EX_APPWINDOW);
        Win32NativeMethods.SetLastError(0);
        Win32NativeMethods.SetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE, updated);
        var error = Win32NativeMethods.LastError;
        if (error != 0)
        {
            Win32NativeMethods.SetWindowLongPtr(hwnd, Win32NativeMethods.GWL_STYLE, currentStyle);
            return Failure("SetWindowLongPtr(GhostStyle)", "Could not remove the target from Alt+Tab.", error);
        }

        RefreshWindowStyle(hwnd);

        return NativeResult.Ok("SetWindowLongPtr(GhostStyle)");
    }

    private NativeResult RestoreOriginalStyle(nint hwnd, WindowSnapshot snapshot)
    {
        Win32NativeMethods.SetLastError(0);
        Win32NativeMethods.SetWindowLongPtr(hwnd, Win32NativeMethods.GWL_STYLE, snapshot.Styles.Style);
        var styleError = Win32NativeMethods.LastError;
        if (styleError != 0)
        {
            return Failure("SetWindowLongPtr(RestoreFrame)", "Could not restore the target window frame.", styleError);
        }

        Win32NativeMethods.SetLastError(0);
        Win32NativeMethods.SetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE, snapshot.Styles.ExtendedStyle);
        var error = Win32NativeMethods.LastError;
        if (error != 0)
        {
            return Failure("SetWindowLongPtr(RestoreStyle)", "Could not restore the target window style.", error);
        }

        RefreshWindowStyle(hwnd);

        return NativeResult.Ok("SetWindowLongPtr(RestoreStyle)");
    }

    private static void RefreshWindowStyle(nint hwnd)
    {
        Win32NativeMethods.SetWindowPos(hwnd, 0, 0, 0, 0, 0,
            Win32NativeMethods.SWP_NOSIZE | Win32NativeMethods.SWP_NOMOVE |
            Win32NativeMethods.SWP_NOZORDER | Win32NativeMethods.SWP_NOACTIVATE |
            Win32NativeMethods.SWP_FRAMECHANGED);
    }

    private void DisableSystemBackdrop(nint hwnd)
    {
        var none = Win32NativeMethods.DWM_SYSTEMBACKDROP_NONE;
        if (Win32NativeMethods.DwmSetWindowAttribute(hwnd, Win32NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int)) != 0)
        {
            _logger.Log(LogLevel.Debug, $"DWM system backdrop is unavailable for hwnd={hwnd}; continuing with region clipping.");
        }
    }

    private void DisableNonClientRendering(nint hwnd)
    {
        var disabled = Win32NativeMethods.DWMNCRP_DISABLED;
        if (Win32NativeMethods.DwmSetWindowAttribute(hwnd, Win32NativeMethods.DWMWA_NCRENDERING_POLICY, ref disabled, sizeof(int)) != 0)
        {
            _logger.Log(LogLevel.Debug, $"DWM non-client rendering is unavailable for hwnd={hwnd}; continuing without it.");
        }
    }

    private void DisableWindowDecorations(nint hwnd)
    {
        var noRound = Win32NativeMethods.DWM_WINDOW_CORNER_DONOTROUND;
        Win32NativeMethods.DwmSetWindowAttribute(hwnd, Win32NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, sizeof(int));
        var noBorder = Win32NativeMethods.DWM_COLOR_NONE;
        Win32NativeMethods.DwmSetWindowAttribute(hwnd, Win32NativeMethods.DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));
    }

    private void RestoreSystemBackdrop(nint hwnd, int? originalType)
    {
        if (originalType is not int value)
        {
            return;
        }

        Win32NativeMethods.DwmSetWindowAttribute(hwnd, Win32NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref value, sizeof(int));
    }

    private void RestoreNonClientRendering(nint hwnd, int? originalPolicy)
    {
        if (originalPolicy is not int value)
        {
            value = Win32NativeMethods.DWMNCRP_USEWINDOWSTYLE;
        }

        Win32NativeMethods.DwmSetWindowAttribute(hwnd, Win32NativeMethods.DWMWA_NCRENDERING_POLICY, ref value, sizeof(int));
    }

    private void RestoreWindowDecorations(nint hwnd, int? originalCorner, int? originalBorder)
    {
        if (originalCorner is int corner)
        {
            Win32NativeMethods.DwmSetWindowAttribute(hwnd, Win32NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }

        if (originalBorder is int border)
        {
            Win32NativeMethods.DwmSetWindowAttribute(hwnd, Win32NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
    }

    private static NativeResult SetTopmost(nint hwnd)
    {
        var flags = Win32NativeMethods.SWP_NOSIZE | Win32NativeMethods.SWP_NOMOVE |
            Win32NativeMethods.SWP_NOACTIVATE;
        return Win32NativeMethods.SetWindowPos(hwnd, Win32NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags)
            ? NativeResult.Ok("SetWindowPos(HWND_TOPMOST)")
            : NativeResult.Failed("SetWindowPos(HWND_TOPMOST)", Win32NativeMethods.LastError, "Could not raise the target window.");
    }

    private static void RestoreTopmost(nint hwnd, nint originalExtendedStyle)
    {
        var insertAfter = ((long)originalExtendedStyle & (long)Win32NativeMethods.WS_EX_TOPMOST) != 0
            ? Win32NativeMethods.HWND_TOPMOST
            : Win32NativeMethods.HWND_NOTOPMOST;
        Win32NativeMethods.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0,
            Win32NativeMethods.SWP_NOSIZE | Win32NativeMethods.SWP_NOMOVE |
            Win32NativeMethods.SWP_NOACTIVATE);
    }

    private static nint CreateRegion(byte[] data)
    {
        var memory = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, memory, data.Length);
            return Win32NativeMethods.ExtCreateRegion(0, (uint)data.Length, memory);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    private NativeResult Failure(string operation, string message, int? error = null)
    {
        var code = error ?? Win32NativeMethods.LastError;
        _logger.Log(LogLevel.Error, $"NativeCallFailed operation={operation} error={code} {message}");
        return NativeResult.Failed(operation, code, message);
    }
}
