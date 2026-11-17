using System.Drawing;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App.Tests;

[Collection("Windows UI")]
public sealed class BackgroundInputNativeStyleTests
{
    [Fact]
    public void Arbitrary_target_shortcuts_and_drag_packets_are_sent_only_to_the_bridge()
    {
        var hwnd = Win32NativeMethods.CreateWindowEx(0, "STATIC", "Bridge routing test", Win32NativeMethods.WS_POPUP,
            -20000, -20000, 100, 100, 0, 0, Win32NativeMethods.GetModuleHandle(null), 0);
        Assert.NotEqual(0, hwnd);
        var windows = new Win32WindowApi();
        var snapshot = windows.CaptureSnapshot(new TargetWindow
        {
            Hwnd = hwnd, ProcessId = (uint)Environment.ProcessId, ScreenBounds = new(-20000, -20000, 100, 100)
        }).Value!;
        var bridge = new TestBridge();
        using var backend = new Win32BackgroundInputBackend(windows, NullLogger.Instance, bridge);
        try
        {
            Assert.True(backend.SetNoActivate(snapshot, false).Success);
            foreach (var (key, down) in new[] { (0xA2, true), (0x41, true), (0x41, false), (0x43, true), (0x43, false), (0x56, true), (0x56, false), (0xA2, false) })
            {
                var input = new BackgroundKeyInput(key, 30, down, false, false);
                backend.UpdateKeyboardState(input);
                var result = backend.DispatchKey(snapshot, hwnd, input);
                Assert.True(result.Result.Success);
                Assert.Equal(BackgroundInputCategory.RawKey, result.Category);
            }
            Assert.Equal(8, bridge.Messages.Count);
            Assert.All(bridge.Messages.Skip(1).Take(6), item => Assert.NotEqual(0, item.Keyboard[0x11] & 0x80));
            var point = new Point(-19980, -19980);
            Assert.True(backend.DispatchMouse(snapshot, hwnd, new(BackgroundMouseAction.Down, BackgroundMouseButton.Left), point).Result.Success);
            Assert.True(backend.DispatchMouse(snapshot, hwnd, new(BackgroundMouseAction.Move), point + new Size(10, 0)).Result.Success);
            Assert.True(backend.DispatchMouse(snapshot, hwnd, new(BackgroundMouseAction.Up, BackgroundMouseButton.Left), point).Result.Success);
            Assert.Equal(new uint[] { 0x0201, 0x0200, 0x0202 }, bridge.Messages.Skip(8).Select(item => item.Message));
            Assert.Equal(new nint[] { 1, 1, 0 }, bridge.Messages.Skip(8).Select(item => item.Flags));
        }
        finally
        {
            backend.SetNoActivate(snapshot, true);
            Win32NativeMethods.DestroyWindow(hwnd);
        }
    }

    [Fact]
    public void Bridge_composition_chord_rejection_preserves_foreground_layout_and_reset_clears_modifiers()
    {
        var hwnd = Win32NativeMethods.CreateWindowEx(0, "STATIC", "GhostSlacking input test", Win32NativeMethods.WS_POPUP,
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
            var snapshot = captured.Value!;
            using var backend = new Win32BackgroundInputBackend(windows, NullLogger.Instance, new TestBridge());
            var foreground = backend.ForegroundWindow;
            var foregroundThread = Win32NativeMethods.GetWindowThreadProcessId(foreground, out _);
            var foregroundLayout = Win32BackgroundInputNative.GetKeyboardLayout(foregroundThread);
            var layout = Win32BackgroundInputNative.GetKeyboardLayout(0);
            Assert.True(backend.SetNoActivate(snapshot, restore: false).Success);

            foreach (var shift in new[] { 0xA0, 0xA1 })
            {
                Assert.Equal(BackgroundInputCategory.RawKey, Key(shift, true).Category);
                Assert.Equal(BackgroundInputCategory.RawKey, Key(shift, false).Category);
                // Shift and shifted keys use the same raw bridge path.
                Key(shift, true);
                Assert.Equal(BackgroundInputCategory.RawKey, Key(0x41, true).Category);
                Key(0x41, false);
                Assert.Null(Key(shift, false).Rejection);
            }

            foreach (var modifier in new[] { 0xA2, 0xA3, 0x5B, 0x5C })
            {
                Key(modifier, true);
                AssertCompositionRejected(Key(0x20, true));
                Assert.False(Key(0x20, false).KeyDownPosted);
                // Reset with a held modifier, then reset again. Later input
                // must not inherit a chord or a foreground text context.
                backend.ResetInputState();
                backend.ResetInputState();
                Assert.Equal(BackgroundInputCategory.RawKey, Key(0x41, true).Category);
                Key(0x41, false);
                Assert.Equal(modifier is 0x5B or 0x5C ? BackgroundInputRejectionReason.SystemShortcut : (BackgroundInputRejectionReason?)null, Key(modifier, false).Rejection);
            }

            Assert.True(backend.SetNoActivate(snapshot, restore: true).Success);
            Assert.Equal(foreground, backend.ForegroundWindow);
            Assert.Equal(layout, Win32BackgroundInputNative.GetKeyboardLayout(0));
            Assert.Equal(foregroundLayout, Win32BackgroundInputNative.GetKeyboardLayout(foregroundThread));
            Assert.Equal(snapshot.Styles.ExtendedStyle,
                Win32NativeMethods.GetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE));

            BackgroundInputDispatchResult Key(int key, bool down)
            {
                uint scanCode = key switch { 0xA0 => 0x2A, 0xA1 => 0x36, 0xA2 or 0xA3 => 0x1D, 0x20 => 0x39, 0x41 => 0x1E, _ => (uint)key };
                var input = new BackgroundKeyInput(key, scanCode, down, key is 0xA3 or 0x5B or 0x5C, false);
                backend.UpdateKeyboardState(input);
                return backend.DispatchKey(snapshot, hwnd, input);
            }

            static void AssertCompositionRejected(BackgroundInputDispatchResult result)
            {
                Assert.Equal(BackgroundInputRejectionReason.ImeComposition, result.Rejection);
                Assert.Equal(BackgroundInputCategory.Ignored, result.Category);
                Assert.False(result.Result.Success);
                Assert.False(result.KeyDownPosted);
            }
        }
        finally
        {
            Win32NativeMethods.DestroyWindow(hwnd);
        }
    }

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
            using var backend = new Win32BackgroundInputBackend(windows, NullLogger.Instance, new TestBridge());
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
            using var backend = new Win32BackgroundInputBackend(windows, NullLogger.Instance, new TestBridge());
            Assert.True(backend.SetNoActivate(captured.Value!, restore: false).Success);
            Assert.True(backend.SetNoActivate(captured.Value!, restore: true).Success);
            Assert.NotEqual(0, (long)Win32NativeMethods.GetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE) & Win32NativeMethods.WS_EX_NOACTIVATE);
        }
        finally
        {
            Win32NativeMethods.DestroyWindow(hwnd);
        }
    }
    private sealed class TestBridge : IExclusiveInputBridge
    {
        public List<(uint Message, nint Flags, byte[] Keyboard)> Messages { get; } = [];
        public NativeResult Start(WindowSnapshot snapshot, nint foreground) => NativeResult.Ok("Fixture");
        public NativeResult Renew() => NativeResult.Ok("Fixture");
        public nint MenuAt(Point cursor) => 0;
        public bool IsMenu(nint window) => false;
        public bool HasMenus => false;
        public nint[] Menus() => [];
        public NativeResult PresentMenus(IReadOnlyList<nint> menus) => NativeResult.Ok("Fixture");
        public NativeResult Send(nint recipient, uint message, nint wparam, nint lparam, byte[] keyboard, Point cursor)
        {
            Messages.Add((message, wparam, (byte[])keyboard.Clone()));
            return NativeResult.Ok("Fixture");
        }
        public void Dispose() { }
    }
}
