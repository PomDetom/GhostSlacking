using System.Drawing;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App.Tests;

public sealed class Win32DisplayTransitionTests
{
    [Fact]
    public void Owned_native_window_stays_hidden_during_simulated_display_change_and_restores_new_geometry()
        => CheckNativeTransition(maximized: false);

    [Fact]
    public void Owned_maximized_window_preserves_native_state_across_simulated_display_changes()
        => CheckNativeTransition(maximized: true);

    private static void CheckNativeTransition(bool maximized)
    {
        // This exercises real HWND/style/region/placement calls. The display
        // context is simulated; it does not change the user's system scaling.
        var hwnd = Win32NativeMethods.CreateWindowEx((uint)Win32NativeMethods.WS_EX_TOOLWINDOW,
            "STATIC", "GhostSlacking isolated DPI test", Win32NativeMethods.WS_POPUP | (maximized ? 0x01000000u : 0u),
            -20000, -20000, 160, 120, 0, 0, Win32NativeMethods.GetModuleHandle(null), 0);
        Assert.NotEqual(0, hwnd);
        try
        {
            var api = new DisplayApi();
            var backend = new Win32VisibilityBackend();
            var recovery = new RecoveryManager(api, backend);
            var time = new ManualTime();
            var coordinator = new GhostCoordinator(api, recovery, new VisibilityEngine(backend), timeProvider: time);
            var errors = new List<string>();
            coordinator.UserErrorOccurred += (_, error) => errors.Add(error.TechnicalMessage);
            var target = new TargetWindow { Hwnd = hwnd, ProcessId = (uint)Environment.ProcessId, ScreenBounds = api.Observe(hwnd)!.ScreenBounds };
            Assert.True(coordinator.SelectWindow(target, new RevealSettings()));
            Assert.Equal(maximized, api.Observe(hwnd)!.IsMaximized);
            foreach (var scale in new[] { 125, 100, 150, 125 })
            {
                api.Scale = scale;
                if (!maximized)
                    Assert.True(Win32NativeMethods.SetWindowPos(hwnd, 0, -20000, -20000, 160 * scale / 100, 120 * scale / 100,
                        Win32NativeMethods.SWP_NOACTIVATE | Win32NativeMethods.SWP_NOZORDER));
                coordinator.UpdatePeek(new Point(-19950, -19950), false);
                Assert.True(coordinator.IsEnvironmentTransitioning);
                Assert.False(Win32NativeMethods.IsWindowVisible(hwnd));
                time.Advance();
                coordinator.UpdatePeek(new Point(-19950, -19950), false);
                Assert.False(coordinator.IsEnvironmentTransitioning);
                Assert.Equal(GhostState.Ghost, coordinator.State);
                Assert.False(Win32NativeMethods.IsWindowVisible(hwnd));
                Assert.Equal(api.Observe(hwnd)!.ScreenBounds, coordinator.CurrentProfile!.Original.ScreenBounds);
                Assert.Equal(maximized, api.Observe(hwnd)!.IsMaximized);
            }
            var snapshot = coordinator.CurrentProfile!.Original;
            var cursor = new Point(snapshot.ScreenBounds.Left + 50, snapshot.ScreenBounds.Top + 50);
            coordinator.UpdatePeek(cursor, true);
            Assert.Equal(GhostState.Reveal, coordinator.State);
            Assert.True(Win32NativeMethods.IsWindowVisible(hwnd));
            coordinator.UpdatePeek(new Point(-19950, -19950), false);
            Assert.False(Win32NativeMethods.IsWindowVisible(hwnd));
            var item = WatchdogRecoveryItem.FromSnapshot(snapshot);
            Assert.True(backend.Restore(hwnd, item.ToSnapshot()).Success);
            Assert.True(WindowPlacementRestoration.Matches(snapshot, api.Observe(hwnd)!));
            Assert.Equal(snapshot.Styles.ExtendedStyle, Win32NativeMethods.GetWindowLongPtr(hwnd, Win32NativeMethods.GWL_EXSTYLE));
            Assert.Empty(errors);
        }
        finally
        {
            Win32NativeMethods.DestroyWindow(hwnd);
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _now;
        public void Advance() => _now += 250;
    }

    private sealed class DisplayApi : IWindowApi, IWindowEnvironmentApi
    {
        private readonly Win32WindowApi _native = new();
        public int Scale { get; set; } = 100;
        public bool IsWindow(nint hwnd) => _native.IsWindow(hwnd);
        public WindowObservation? Observe(nint hwnd) => _native.Observe(hwnd);
        public OperationResult<WindowSnapshot> CaptureSnapshot(TargetWindow target) => _native.CaptureSnapshot(target);
        public bool IsSameIdentity(WindowSnapshot snapshot, WindowObservation observation) => _native.IsSameIdentity(snapshot, observation);
        public Point GetCursorPosition() => Point.Empty;
        public OperationResult<WindowEnvironment> ReadEnvironment(WindowSnapshot original)
        {
            var read = _native.ReadEnvironment(original);
            return read.Success && read.Value is { } value
                ? OperationResult<WindowEnvironment>.Ok(value with { Display = value.Display with { ScalePercent = Scale } })
                : read;
        }
    }
}
