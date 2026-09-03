using GhostSlacking.Core;
using System.Drawing;

namespace GhostSlacking.Core.Tests;

public sealed class CoordinatorTests
{
    [Fact]
    public void Reveal_only_updates_when_cursor_or_window_geometry_changes()
    {
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend();
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(api, recovery, new VisibilityEngine(backend));
        var target = api.Target;

        Assert.True(coordinator.SelectWindow(target, new RevealSettings()));
        // A real Ghost window is hidden with SW_HIDE, so visibility must not
        // prevent a Peek reveal.
        api.Observation = api.Observation with { IsVisible = false };
        coordinator.UpdatePeek(new Point(200, 200), true);
        coordinator.UpdatePeek(new Point(200, 200), true);
        coordinator.UpdatePeek(new Point(201, 200), true);

        Assert.Equal(2, backend.RevealCalls);
        Assert.Equal(GhostState.Reveal, coordinator.State);
        coordinator.UpdatePeek(new Point(800, 800), true);
        Assert.Equal(GhostState.Ghost, coordinator.State);
        Assert.True(coordinator.RestoreCurrent("test"));
        Assert.Equal(1, backend.RestoreCalls);
    }

    [Fact]
    public void Target_identity_mismatch_is_not_restored_to_a_reused_handle()
    {
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend();
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(api, recovery, new VisibilityEngine(backend));
        Assert.True(coordinator.SelectWindow(api.Target, new RevealSettings()));

        api.Observation = api.Observation with { ProcessId = api.Target.ProcessId + 1 };
        coordinator.UpdatePeek(Point.Empty, false);

        Assert.Equal(GhostState.Idle, coordinator.State);
        Assert.Equal(0, backend.RestoreCalls);
    }

    private sealed class FakeWindowApi : IWindowApi
    {
        public TargetWindow Target { get; } = new()
        {
            Hwnd = 42,
            ProcessId = 100,
            Title = "Test",
            ProcessName = "test",
            ScreenBounds = new Rectangle(100, 100, 500, 400)
        };

        public WindowObservation Observation { get; set; }

        public FakeWindowApi()
        {
            Observation = new WindowObservation
            {
                Hwnd = Target.Hwnd,
                ProcessId = Target.ProcessId,
                ScreenBounds = Target.ScreenBounds,
                IsVisible = true,
                IsMinimized = false,
                ProcessName = Target.ProcessName
            };
        }

        public bool IsWindow(nint hwnd) => Observation.Hwnd == hwnd;
        public WindowObservation? Observe(nint hwnd) => IsWindow(hwnd) ? Observation : null;
        public Point GetCursorPosition() => Point.Empty;

        public OperationResult<WindowSnapshot> CaptureSnapshot(TargetWindow target) => OperationResult<WindowSnapshot>.Ok(new WindowSnapshot
        {
            Hwnd = target.Hwnd,
            ProcessId = target.ProcessId,
            ProcessName = target.ProcessName,
            ScreenBounds = target.ScreenBounds,
            WasVisible = true,
            WasMinimized = false,
            Styles = new WindowStyleSnapshot(0, 0),
            CapturedAt = DateTimeOffset.UtcNow,
            ProcessStartIdentity = "start"
        });

        public bool IsSameIdentity(WindowSnapshot snapshot, WindowObservation observation) =>
            snapshot.Hwnd == observation.Hwnd && snapshot.ProcessId == observation.ProcessId;
    }

    private sealed class FakeVisibilityBackend : IVisibilityBackend
    {
        public int RevealCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public NativeResult ApplyGhost(nint hwnd) => NativeResult.Ok("Ghost");
        public NativeResult ApplyReveal(nint hwnd, CircleRegion region) { RevealCalls++; return NativeResult.Ok("Reveal"); }
        public NativeResult Restore(nint hwnd, WindowSnapshot snapshot) { RestoreCalls++; return NativeResult.Ok("Restore"); }
    }
}
