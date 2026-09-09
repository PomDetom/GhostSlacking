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
    public void Overlay_window_applies_ring_and_disposes_repeatedly()
    {
        var window = new Win32OverlayWindow();
        var targetBounds = new Rectangle(-32000, -32000, 100, 100);
        var outer = new CircleRegion(50, 50, 80, RevealShape.RoundedRectangle, 16);
        var inner = new CircleRegion(50, 50, 40, RevealShape.RoundedRectangle, 8);

        Assert.True(window.SetBounds(targetBounds).Success);
        Assert.True(window.SetRingRegion(outer, inner, targetBounds, targetBounds).Success);
        Assert.True(window.ShowTopmostNoActivate().Success);
        window.Hide();
        window.Dispose();
        window.Dispose();
        Assert.Equal(0, window.Handle);
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
