using System.Runtime.InteropServices;
using System.Drawing;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App.Tests;

public sealed class Win32MessageWindowTests
{
    [Fact]
    public void Message_window_dispatches_hotkey_and_disposes_repeatedly()
    {
        var window = new Win32MessageWindow();
        var received = 0;
        window.HotkeyPressed += id => received = id;

        SendMessage(window.Handle, 0x0312, 42, 0);

        Assert.Equal(42, received);
        Assert.NotEqual(0, window.Handle);
        window.Dispose();
        window.Dispose();
        Assert.Equal(0, window.Handle);
    }

    [Fact]
    public void Close_message_requests_shutdown_only_once()
    {
        using var window = new Win32MessageWindow();
        var closeRequests = 0;
        window.CloseRequested += () => closeRequests++;

        SendMessage(window.Handle, 0x0010, 0, 0);
        SendMessage(window.Handle, 0x0010, 0, 0);

        Assert.Equal(1, closeRequests);
        Assert.NotEqual(0, window.Handle);
    }

    [Fact]
    public void Display_change_message_invalidates_display_configuration()
    {
        using var window = new Win32MessageWindow();
        var notifications = 0;
        window.DisplayConfigurationChanged += () => notifications++;

        SendMessage(window.Handle, 0x007E, 0, 0);

        Assert.Equal(1, notifications);
    }

    [Fact]
    public void Foreground_activation_rejects_an_invalid_window_handle()
    {
        var result = new Win32WindowApi().BringToForeground(0);

        Assert.False(result.Success);
        Assert.Equal("SetForegroundWindow", result.Operation);
    }

    [Fact]
    public void Overlay_window_applies_ring_and_disposes_repeatedly()
    {
        using var owner = new Win32MessageWindow();
        var window = new Win32OverlayWindow(owner.Handle);
        var targetBounds = new Rectangle(-32000, -32000, 100, 100);
        var outer = new CircleRegion(50, 50, 80, RevealShape.RoundedRectangle, 16);
        var inner = new CircleRegion(50, 50, 40, RevealShape.RoundedRectangle, 8);

        Assert.True(window.SetBounds(targetBounds).Success);
        Assert.True(window.SetRingRegion(outer, inner).Success);
        Assert.True(window.ShowTopmostNoActivate().Success);
        window.Hide();
        window.Dispose();
        window.Dispose();
        Assert.Equal(0, window.Handle);
    }

    [Fact]
    public void Overlay_window_is_owned_by_and_stays_above_the_target()
    {
        using var owner = new Win32MessageWindow();
        using var window = new Win32OverlayWindow(owner.Handle);
        var flags = SwpNoSize | SwpNoMove | SwpShowWindow;

        Assert.Equal(owner.Handle, GetWindow(window.Handle, GwOwner));
        Assert.True(SetWindowPos(owner.Handle, HwndTopmost, 0, 0, 0, 0, flags));
        Assert.True(window.ShowTopmostNoActivate().Success);
        Assert.True(SetWindowPos(owner.Handle, HwndTopmost, 0, 0, 0, 0, flags));
        Assert.True(IsAbove(window.Handle, owner.Handle));
    }

    [Fact]
    public void Destroying_the_owner_invalidates_the_overlay_handle_and_allows_recreation()
    {
        var owner = new Win32MessageWindow();
        var window = new Win32OverlayWindow(owner.Handle);

        owner.Dispose();

        Assert.Equal(0, window.Handle);
        window.Hide();
        window.Dispose();
        window.Dispose();

        using var replacementOwner = new Win32MessageWindow();
        using var replacement = new Win32OverlayWindow(replacementOwner.Handle);
        Assert.NotEqual(0, replacement.Handle);
        Assert.Equal(replacementOwner.Handle, GetWindow(replacement.Handle, GwOwner));
    }

    private static bool IsAbove(nint expectedHigher, nint expectedLower)
    {
        var higherSeen = false;
        var lowerSeenAfterHigher = false;
        EnumWindows((handle, _) =>
        {
            if (handle == expectedHigher)
            {
                higherSeen = true;
            }
            else if (handle == expectedLower)
            {
                lowerSeenAfterHigher = higherSeen;
                return false;
            }

            return true;
        }, 0);
        return lowerSeenAfterHigher;
    }

    private const uint GwOwner = 4;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTopmost = -1;

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hwnd, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hwnd,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
}
