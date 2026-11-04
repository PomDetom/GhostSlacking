using System.Drawing;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App.Tests;

[Collection("Windows UI")]
public sealed class BackgroundInputNativeStyleTests
{
    [Fact]
    public void Native_session_style_restores_only_its_bit_and_rejects_a_stale_process_identity()
    {
        var hwnd = Win32NativeMethods.CreateWindowEx(0, "STATIC", "GhostSlacking style test", Win32NativeMethods.WS_POPUP,
            -20000, -20000, 100, 100, 0, 0, Win32NativeMethods.GetModuleHandle(null), 0);
        Assert.NotEqual(0, hwnd);
        try
        {
            var windows = new Win32WindowApi();
            var recovery = new RecoveryManager(windows, new Win32VisibilityBackend());
            var captured = recovery.CaptureAndRegister(new TargetWindow
            {
                Hwnd = hwnd, ProcessId = (uint)Environment.ProcessId, ScreenBounds = new Rectangle(-20000, -20000, 100, 100)
            }, new RevealSettings());
            Assert.True(captured.Success, captured.Error);
            var snapshot = captured.Value!.Original;
            using var backend = new Win32BackgroundInputBackend(windows, NullLogger.Instance);
            var foreground = backend.ForegroundWindow;

            // Simulate the existing Ghost style, applied after recovery registration.
            var ghostStyle = (nint)((long)snapshot.Styles.ExtendedStyle | (long)Win32NativeMethods.WS_EX_TOOLWINDOW);
            Win32NativeMethods.SetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE, ghostStyle);
            Assert.True(backend.SetNoActivate(snapshot, restore: false).Success);
            var activeStyle = Win32NativeMethods.GetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE);
            Assert.NotEqual(0, (long)activeStyle & Win32NativeMethods.WS_EX_NOACTIVATE);
            Assert.False(backend.SetNoActivate(snapshot with { ProcessStartIdentity = "stale-process-start" }, restore: true).Success);
            Assert.Equal(activeStyle, Win32NativeMethods.GetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE));
            Assert.True(backend.SetNoActivate(snapshot, restore: true).Success);
            Assert.Equal(ghostStyle, Win32NativeMethods.GetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE));
            Assert.Equal(foreground, backend.ForegroundWindow);

            var manifest = RecoveryManifest.FromProfiles(Guid.NewGuid(), recovery.Profiles, DateTimeOffset.UtcNow);
            var roundTrip = WatchdogProtocol.Deserialize(WatchdogProtocol.Serialize(new WatchdogMessage
            {
                SessionId = manifest.SessionId, Kind = WatchdogMessageKind.RecoveryManifest,
                SentAtUtc = DateTimeOffset.UtcNow, Manifest = manifest
            }));
            Assert.Equal(snapshot.Styles.ExtendedStyle.ToInt64(), Assert.Single(roundTrip.Manifest!.Items).ExtendedStyle);
        }
        finally
        {
            Win32NativeMethods.DestroyWindow(hwnd);
        }
    }

    [Fact]
    public void An_original_noactivate_style_is_preserved_when_the_session_ends()
    {
        var hwnd = Win32NativeMethods.CreateWindowEx(Win32NativeMethods.WS_EX_NOACTIVATE, "STATIC", "GhostSlacking style test", Win32NativeMethods.WS_POPUP,
            -20000, -20000, 100, 100, 0, 0, Win32NativeMethods.GetModuleHandle(null), 0);
        Assert.NotEqual(0, hwnd);
        try
        {
            var windows = new Win32WindowApi();
            var captured = windows.CaptureSnapshot(new TargetWindow
            {
                Hwnd = hwnd, ProcessId = (uint)Environment.ProcessId, ScreenBounds = new Rectangle(-20000, -20000, 100, 100)
            });
            Assert.True(captured.Success, captured.Error);
            using var backend = new Win32BackgroundInputBackend(windows, NullLogger.Instance);
            Assert.True(backend.SetNoActivate(captured.Value!, restore: false).Success);
            Assert.True(backend.SetNoActivate(captured.Value!, restore: true).Success);
            Assert.NotEqual(0, (long)Win32NativeMethods.GetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE) & Win32NativeMethods.WS_EX_NOACTIVATE);
        }
        finally
        {
            Win32NativeMethods.DestroyWindow(hwnd);
        }
    }
}
