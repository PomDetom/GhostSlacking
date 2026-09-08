using System.Runtime.InteropServices;
using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

public sealed class Win32VisibilityBackend : IVisibilityBackend
{
    private const int RevealRegionValidationAttempts = 3;
    private readonly ILogger _logger;

    public Win32VisibilityBackend(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    public NativeResult ApplyGhost(nint hwnd, WindowSnapshot snapshot)
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
        var preserved = EnsureWindowPlacement(hwnd, snapshot);
        Win32NativeMethods.ShowWindow(hwnd, Win32NativeMethods.SW_HIDE);
        return preserved;
    }

    public NativeResult ApplyReveal(nint hwnd, CircleRegion region, WindowSnapshot snapshot)
    {
        var applied = ApplyRevealRegion(hwnd, region);
        if (!applied.Success)
        {
            return applied;
        }

        // Install the first region while hidden, then use SWP_SHOWWINDOW so the
        // existing normal/maximized placement is shown without a ShowWindow
        // command that can reinterpret the browser's state.
        if (!Win32NativeMethods.SetWindowPos(
                hwnd,
                0,
                0,
                0,
                0,
                0,
                Win32NativeMethods.SWP_NOMOVE | Win32NativeMethods.SWP_NOSIZE |
                Win32NativeMethods.SWP_NOZORDER | Win32NativeMethods.SWP_NOACTIVATE |
                Win32NativeMethods.SWP_SHOWWINDOW))
        {
            return Failure("SetWindowPos(SHOWWINDOW)", "Could not reveal the target without changing its placement.");
        }

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

        return EnsureWindowPlacement(hwnd, snapshot);
    }

    public NativeResult EnsureWindowPlacement(nint hwnd, WindowSnapshot snapshot)
    {
        if (!Win32NativeMethods.IsWindow(hwnd))
        {
            return Failure("SetWindowPlacement(Preserve)", "The target window no longer exists.");
        }

        if (snapshot.ScreenBounds.Width <= 0 || snapshot.ScreenBounds.Height <= 0)
        {
            return Failure("SetWindowPlacement(Preserve)", $"The expected bounds are invalid: {snapshot.ScreenBounds}.");
        }

        if (!Win32NativeMethods.GetWindowRect(hwnd, out var current))
        {
            return Failure("GetWindowRect(PreservePlacement)", "Could not read the target window bounds.");
        }

        var currentBounds = Rectangle.FromLTRB(current.Left, current.Top, current.Right, current.Bottom);
        var placementMatches = snapshot.Placement.IsMinimized
            ? Win32NativeMethods.IsIconic(hwnd)
            : snapshot.Placement.IsMaximized
                ? Win32NativeMethods.IsZoomed(hwnd) && currentBounds == snapshot.ScreenBounds
                : !Win32NativeMethods.IsIconic(hwnd) &&
                  !Win32NativeMethods.IsZoomed(hwnd) &&
                  currentBounds == snapshot.ScreenBounds;
        if (placementMatches)
        {
            return NativeResult.Ok("PreserveWindowPlacement");
        }

        if (!snapshot.Placement.IsMinimized && !snapshot.Placement.IsMaximized)
        {
            return Win32NativeMethods.SetWindowPos(
                hwnd,
                0,
                snapshot.ScreenBounds.Left,
                snapshot.ScreenBounds.Top,
                snapshot.ScreenBounds.Width,
                snapshot.ScreenBounds.Height,
                Win32NativeMethods.SWP_NOZORDER | Win32NativeMethods.SWP_NOACTIVATE)
                ? NativeResult.Ok("SetWindowPos(PreservePlacement)")
                : Failure("SetWindowPos(PreservePlacement)", "Could not preserve the target window placement.");
        }

        var wasVisible = Win32NativeMethods.IsWindowVisible(hwnd);
        var placement = ToNativePlacement(snapshot.Placement);
        if (!Win32NativeMethods.SetWindowPlacement(hwnd, ref placement))
        {
            return Failure("SetWindowPlacement(Preserve)", "Could not preserve the target window state.");
        }

        if (!wasVisible)
        {
            Win32NativeMethods.ShowWindow(hwnd, Win32NativeMethods.SW_HIDE);
        }

        return NativeResult.Ok("SetWindowPlacement(Preserve)");
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

        var placement = ToNativePlacement(snapshot.Placement);
        if (!Win32NativeMethods.SetWindowPlacement(hwnd, ref placement))
        {
            return Failure("SetWindowPlacement(Restore)", "Could not restore the target window placement.");
        }

        RestoreTopmost(hwnd, snapshot.Styles.ExtendedStyle);

        if (!snapshot.WasVisible)
        {
            Win32NativeMethods.ShowWindow(hwnd, Win32NativeMethods.SW_HIDE);
        }
        else if (!Win32NativeMethods.IsWindowVisible(hwnd) &&
                 !Win32NativeMethods.SetWindowPos(
                     hwnd,
                     0,
                     0,
                     0,
                     0,
                     0,
                     Win32NativeMethods.SWP_NOMOVE | Win32NativeMethods.SWP_NOSIZE |
                     Win32NativeMethods.SWP_NOZORDER | Win32NativeMethods.SWP_NOACTIVATE |
                     Win32NativeMethods.SWP_SHOWWINDOW))
        {
            return Failure("SetWindowPos(RestoreVisibility)", "Could not restore the target window visibility.");
        }

        var placementRestored = EnsureWindowPlacement(hwnd, snapshot);
        return placementRestored.Success ? NativeResult.Ok("Restore") : placementRestored;
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
        NativeResult? lastValidation = null;
        for (var attempt = 1; attempt <= RevealRegionValidationAttempts; attempt++)
        {
            // Elliptic and round-rect regions lose the last raster row/column
            // when their right and bottom coordinates equal the requested
            // bounds, so compensate for both curved shapes. Rectangular GDI
            // regions already retain the exact box.
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

            lastValidation = ValidateRegion(hwnd, bounds);
            if (lastValidation.Success)
            {
                return lastValidation;
            }

            if (attempt < RevealRegionValidationAttempts)
            {
                _logger.Log(
                    LogLevel.Debug,
                    $"Reveal region validation was transient; reapplying hwnd={hwnd} attempt={attempt + 1}/{RevealRegionValidationAttempts}.");
            }
        }

        return Failure(
            lastValidation?.Operation ?? "ValidateRevealRegion",
            lastValidation?.ErrorMessage ?? "The target window did not retain the Reveal region.",
            lastValidation?.ErrorCode);
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
            Win32NativeMethods.SetLastError(0);
            var result = Win32NativeMethods.GetWindowRgn(hwnd, probe);
            if (result == Win32NativeMethods.ERRORREGION)
            {
                return NativeResult.Failed(
                    "GetWindowRgn(Validate)",
                    Win32NativeMethods.LastError,
                    "The target window did not retain the Reveal region.");
            }

            Win32NativeMethods.SetLastError(0);
            var boxResult = Win32NativeMethods.GetRgnBox(probe, out var actual);
            if (boxResult == Win32NativeMethods.ERRORREGION)
            {
                return NativeResult.Failed(
                    "GetRgnBox(Validate)",
                    Win32NativeMethods.LastError,
                    "Could not inspect the target window Reveal region.");
            }

            if (result == Win32NativeMethods.NULLREGION || boxResult == Win32NativeMethods.NULLREGION)
            {
                return NativeResult.Failed(
                    "GetRgnBox(Validate)",
                    Win32NativeMethods.LastError,
                    "The target window Reveal region is empty.");
            }

            var actualBounds = Rectangle.FromLTRB(actual.Left, actual.Top, actual.Right, actual.Bottom);
            if (actualBounds != expected)
            {
                return NativeResult.Failed(
                    "GetRgnBox(Validate)",
                    0,
                    $"Reveal region mismatch; expected={expected}, actual={actualBounds}.");
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
        var current = Win32NativeMethods.GetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE);
        var updated = (nint)((long)current | (long)Win32NativeMethods.WS_EX_TOOLWINDOW);
        updated = (nint)((long)updated & ~(long)Win32NativeMethods.WS_EX_APPWINDOW);
        if (updated == current)
        {
            return NativeResult.Ok("SetWindowLongPtr(GhostStyle)");
        }

        Win32NativeMethods.SetLastError(0);
        Win32NativeMethods.SetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE, updated);
        var error = Win32NativeMethods.LastError;
        if (error != 0)
        {
            return Failure("SetWindowLongPtr(GhostStyle)", "Could not remove the target from Alt+Tab.", error);
        }

        RefreshWindowStyle(hwnd);

        return NativeResult.Ok("SetWindowLongPtr(GhostStyle)");
    }

    private NativeResult RestoreOriginalStyle(nint hwnd, WindowSnapshot snapshot)
    {
        var changed = false;
        var currentStyle = Win32NativeMethods.GetWindowLongPtr(hwnd, Win32NativeMethods.GWL_STYLE);
        if (currentStyle != snapshot.Styles.Style)
        {
            Win32NativeMethods.SetLastError(0);
            Win32NativeMethods.SetWindowLongPtr(hwnd, Win32NativeMethods.GWL_STYLE, snapshot.Styles.Style);
            var styleError = Win32NativeMethods.LastError;
            if (styleError != 0)
            {
                return Failure("SetWindowLongPtr(RestoreFrame)", "Could not restore the target window frame.", styleError);
            }
            changed = true;
        }

        var currentExtendedStyle = Win32NativeMethods.GetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE);
        if (currentExtendedStyle != snapshot.Styles.ExtendedStyle)
        {
            Win32NativeMethods.SetLastError(0);
            Win32NativeMethods.SetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE, snapshot.Styles.ExtendedStyle);
            var error = Win32NativeMethods.LastError;
            if (error != 0)
            {
                return Failure("SetWindowLongPtr(RestoreStyle)", "Could not restore the target window style.", error);
            }
            changed = true;
        }

        if (changed)
        {
            RefreshWindowStyle(hwnd);
        }

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

    private static Win32NativeMethods.WINDOWPLACEMENT ToNativePlacement(WindowPlacementSnapshot snapshot) => new()
    {
        Length = (uint)Marshal.SizeOf<Win32NativeMethods.WINDOWPLACEMENT>(),
        Flags = (uint)snapshot.Flags,
        ShowCmd = (uint)snapshot.ShowCommand,
        MinPosition = new Win32NativeMethods.POINT
        {
            X = snapshot.MinPosition.X,
            Y = snapshot.MinPosition.Y
        },
        MaxPosition = new Win32NativeMethods.POINT
        {
            X = snapshot.MaxPosition.X,
            Y = snapshot.MaxPosition.Y
        },
        NormalPosition = new Win32NativeMethods.RECT
        {
            Left = snapshot.NormalPosition.Left,
            Top = snapshot.NormalPosition.Top,
            Right = snapshot.NormalPosition.Right,
            Bottom = snapshot.NormalPosition.Bottom
        }
    };

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
