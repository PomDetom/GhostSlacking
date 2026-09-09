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
        coordinator.UpdatePeek(new Point(201, 200), true);

        Assert.Equal(2, backend.RevealCalls);
        Assert.Equal(GhostState.Reveal, coordinator.State);
        coordinator.UpdatePeek(new Point(800, 800), true);
        Assert.Equal(GhostState.Ghost, coordinator.State);
        Assert.True(coordinator.RestoreCurrent("test"));
        api.Observation = api.Observation with { IsVisible = true };
        CompleteRestore(coordinator);
        Assert.Equal(1, backend.RestoreCalls);
    }

    [Fact]
    public void Updating_reveal_settings_invalidates_the_cached_region()
    {
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend();
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(api, recovery, new VisibilityEngine(backend));

        Assert.True(coordinator.SelectWindow(api.Target, new RevealSettings()));
        coordinator.UpdatePeek(new Point(200, 200), true);

        Assert.True(coordinator.UpdateRevealSettings(new RevealSettings
        {
            DiameterPx = 320,
            BlurLevel = RevealBlurLevel.High
        }));
        coordinator.UpdatePeek(new Point(200, 200), true);

        Assert.Equal(2, backend.RevealCalls);
        Assert.Equal(320, backend.LastReveal?.DiameterPx);
        Assert.Equal(320, coordinator.CurrentProfile?.Reveal.DiameterPx);
        Assert.Equal(RevealBlurLevel.High, coordinator.CurrentProfile?.Reveal.BlurLevel);
    }

    [Fact]
    public void Transient_reveal_region_validation_failure_is_hidden_and_retried_without_user_error()
    {
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend();
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(api, recovery, new VisibilityEngine(backend));
        var userErrors = new List<UserErrorEventArgs>();
        coordinator.UserErrorOccurred += (_, error) => userErrors.Add(error);
        backend.RevealResults.Enqueue(NativeResult.Failed(
            "GetWindowRgn(Validate)",
            0,
            "The target window did not retain the Reveal region."));

        Assert.True(coordinator.SelectWindow(api.Target, new RevealSettings()));
        api.Observation = api.Observation with { IsVisible = false };
        coordinator.UpdatePeek(new Point(200, 200), true);

        Assert.Equal(GhostState.Ghost, coordinator.State);
        Assert.Empty(userErrors);

        coordinator.UpdatePeek(new Point(200, 200), true);

        Assert.Equal(GhostState.Reveal, coordinator.State);
        Assert.Equal(2, backend.RevealCalls);
        Assert.Empty(userErrors);
    }

    [Fact]
    public void Repeated_reveal_region_validation_failures_eventually_notify_the_user()
    {
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend();
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(api, recovery, new VisibilityEngine(backend));
        var userErrors = new List<UserErrorEventArgs>();
        coordinator.UserErrorOccurred += (_, error) => userErrors.Add(error);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            backend.RevealResults.Enqueue(NativeResult.Failed(
                "GetWindowRgn(Validate)",
                0,
                "The target window did not retain the Reveal region."));
        }

        Assert.True(coordinator.SelectWindow(api.Target, new RevealSettings()));
        api.Observation = api.Observation with { IsVisible = false };
        coordinator.UpdatePeek(new Point(200, 200), true);
        coordinator.UpdatePeek(new Point(200, 200), true);
        coordinator.UpdatePeek(new Point(200, 200), true);

        Assert.Equal(GhostState.Ghost, coordinator.State);
        var error = Assert.Single(userErrors);
        Assert.Equal(UserErrorKind.RevealWindowFailed, error.Kind);
        Assert.Equal("The target window did not retain the Reveal region.", error.TechnicalMessage);
    }

    [Fact]
    public void Feather_host_is_prepared_before_the_expanded_target_region_is_applied()
    {
        var calls = new List<string>();
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend(calls);
        var visualHost = new FakeRevealVisualHost(calls);
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(
            api,
            recovery,
            new VisibilityEngine(backend),
            revealVisualHost: visualHost);

        Assert.True(coordinator.SelectWindow(api.Target, new RevealSettings()));
        calls.Clear();
        coordinator.UpdatePeek(new Point(200, 200), true);

        Assert.Equal(["Prepare", "Reveal", "Present"], calls);
        Assert.Equal(168, backend.LastReveal?.DiameterPx);
        Assert.Equal(144, visualHost.LastVisual?.CoreRegion.DiameterPx);
        Assert.Equal(168, visualHost.LastVisual?.ContentRegion.DiameterPx);
    }

    [Fact]
    public void Feather_prepare_failure_falls_back_to_the_original_hard_edge()
    {
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend();
        var visualHost = new FakeRevealVisualHost
        {
            PrepareResult = NativeResult.Failed("Prepare", 5, "unavailable")
        };
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(
            api,
            recovery,
            new VisibilityEngine(backend),
            revealVisualHost: visualHost);

        Assert.True(coordinator.SelectWindow(api.Target, new RevealSettings()));
        coordinator.UpdatePeek(new Point(200, 200), true);

        Assert.Equal(GhostState.Reveal, coordinator.State);
        Assert.Equal(144, backend.LastReveal?.DiameterPx);
        Assert.True(visualHost.HideCalls > 0);
    }

    [Fact]
    public void Feather_presentation_failure_immediately_reapplies_the_original_hard_edge()
    {
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend();
        var visualHost = new FakeRevealVisualHost
        {
            PresentResult = NativeResult.Failed("Present", 5, "denied")
        };
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(
            api,
            recovery,
            new VisibilityEngine(backend),
            revealVisualHost: visualHost);

        Assert.True(coordinator.SelectWindow(api.Target, new RevealSettings()));
        coordinator.UpdatePeek(new Point(200, 200), true);

        Assert.Equal(GhostState.Reveal, coordinator.State);
        Assert.Equal(2, backend.RevealCalls);
        Assert.Equal(144, backend.LastReveal?.DiameterPx);
        Assert.True(visualHost.HideCalls > 0);
    }

    [Fact]
    public void Lost_compositor_shrinks_an_active_feather_to_the_clear_core_on_the_next_tick()
    {
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend();
        var visualHost = new FakeRevealVisualHost();
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(
            api,
            recovery,
            new VisibilityEngine(backend),
            revealVisualHost: visualHost);

        Assert.True(coordinator.SelectWindow(api.Target, new RevealSettings()));
        coordinator.UpdatePeek(new Point(200, 200), true);
        Assert.Equal(168, backend.LastReveal?.DiameterPx);

        visualHost.IsAvailable = false;
        coordinator.UpdatePeek(new Point(200, 200), true);

        Assert.Equal(2, backend.RevealCalls);
        Assert.Equal(144, backend.LastReveal?.DiameterPx);
        Assert.True(visualHost.HideCalls > 0);
    }

    [Fact]
    public void Leaving_reveal_hides_the_feather_visual()
    {
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend();
        var visualHost = new FakeRevealVisualHost();
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(
            api,
            recovery,
            new VisibilityEngine(backend),
            revealVisualHost: visualHost);

        Assert.True(coordinator.SelectWindow(api.Target, new RevealSettings()));
        coordinator.UpdatePeek(new Point(200, 200), true);
        var hideCallsBefore = visualHost.HideCalls;

        coordinator.UpdatePeek(new Point(200, 200), false);

        Assert.Equal(GhostState.Ghost, coordinator.State);
        Assert.Equal(hideCallsBefore + 1, visualHost.HideCalls);
    }

    [Fact]
    public void Ghost_window_size_drift_is_corrected_before_reveal_geometry_updates()
    {
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend();
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(api, recovery, new VisibilityEngine(backend));

        Assert.True(coordinator.SelectWindow(api.Target, new RevealSettings()));
        api.Observation = api.Observation with
        {
            ScreenBounds = new Rectangle(100, 100, 540, 430)
        };

        coordinator.UpdatePeek(new Point(200, 200), true);

        Assert.Equal(1, backend.EnsurePlacementCalls);
        Assert.Equal(api.Target.ScreenBounds, backend.LastExpectedSnapshot?.ScreenBounds);
    }

    [Fact]
    public void Failed_size_correction_restores_the_window_without_attempting_reveal()
    {
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend
        {
            EnsurePlacementResult = NativeResult.Failed("EnsurePlacement", 5, "denied")
        };
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(api, recovery, new VisibilityEngine(backend));

        Assert.True(coordinator.SelectWindow(api.Target, new RevealSettings()));
        api.Observation = api.Observation with
        {
            ScreenBounds = new Rectangle(100, 100, 540, 430)
        };

        coordinator.UpdatePeek(new Point(200, 200), true);
        api.Observation = api.Observation with { ScreenBounds = api.Target.ScreenBounds, IsVisible = true };
        CompleteRestore(coordinator);

        Assert.Equal(1, backend.EnsurePlacementCalls);
        Assert.Equal(0, backend.RevealCalls);
        Assert.Equal(1, backend.RestoreCalls);
        Assert.Equal(GhostState.Idle, coordinator.State);
        Assert.Null(coordinator.CurrentProfile);
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

    [Fact]
    public void Window_visibility_hotkey_toggles_without_forgetting_the_profile()
    {
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend();
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(api, recovery, new VisibilityEngine(backend));

        Assert.True(coordinator.SelectWindow(api.Target, new RevealSettings()));
        api.Observation = api.Observation with { IsVisible = false };

        Assert.True(coordinator.ToggleWindowVisibility("test"));
        api.Observation = api.Observation with { IsVisible = true };
        CompleteRestore(coordinator);
        Assert.Equal(GhostState.Visible, coordinator.State);
        Assert.NotNull(coordinator.CurrentProfile);
        Assert.Equal(1, backend.RestoreCalls);

        Assert.True(coordinator.ToggleWindowVisibility("test"));
        Assert.Equal(GhostState.Ghost, coordinator.State);
        Assert.Equal(2, backend.GhostCalls);
        Assert.True(coordinator.RestoreCurrent("test"));
        CompleteRestore(coordinator);
        Assert.Null(coordinator.CurrentProfile);
    }

    [Fact]
    public void Window_visibility_action_without_a_target_does_not_enter_window_picking()
    {
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend();
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(api, recovery, new VisibilityEngine(backend));

        Assert.False(coordinator.ToggleWindowVisibility("test"));
        Assert.Equal(GhostState.Idle, coordinator.State);
        Assert.Null(coordinator.CurrentProfile);
        Assert.Equal(0, backend.GhostCalls);
        Assert.Equal(0, backend.RestoreCalls);
    }

    [Fact]
    public void Cancelling_window_picking_preserves_a_visible_target_state()
    {
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend();
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(api, recovery, new VisibilityEngine(backend));
        Assert.True(coordinator.SelectWindow(api.Target, new RevealSettings()));
        Assert.True(coordinator.ToggleWindowVisibility("test"));
        CompleteRestore(coordinator);
        Assert.Equal(GhostState.Visible, coordinator.State);

        coordinator.BeginPicking();
        coordinator.CancelPicking();

        Assert.Equal(GhostState.Visible, coordinator.State);
        Assert.NotNull(coordinator.CurrentProfile);
    }

    [Fact]
    public void Cancelling_window_picking_preserves_an_active_reveal_state()
    {
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend();
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(api, recovery, new VisibilityEngine(backend));
        Assert.True(coordinator.SelectWindow(api.Target, new RevealSettings()));
        coordinator.UpdatePeek(new Point(200, 200), true);
        Assert.Equal(GhostState.Reveal, coordinator.State);

        coordinator.BeginPicking();
        coordinator.CancelPicking();

        Assert.Equal(GhostState.Reveal, coordinator.State);
        Assert.Equal(1, backend.RevealCalls);
    }

    [Fact]
    public void Delayed_restore_drift_is_corrected_and_profile_is_kept_until_three_stable_ticks()
    {
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend();
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(api, recovery, new VisibilityEngine(backend));
        Assert.True(coordinator.SelectWindow(api.Target, new RevealSettings()));

        Assert.True(coordinator.RestoreCurrent("test"));
        coordinator.UpdatePeek(Point.Empty, false);
        Assert.NotNull(coordinator.CurrentProfile);

        api.Observation = api.Observation with { ScreenBounds = new Rectangle(90, 80, 540, 430) };
        coordinator.UpdatePeek(Point.Empty, false);
        Assert.Equal(2, backend.RestoreCalls);
        Assert.NotNull(coordinator.CurrentProfile);

        api.Observation = api.Observation with { ScreenBounds = api.Target.ScreenBounds };
        CompleteRestore(coordinator);

        Assert.Equal(GhostState.Idle, coordinator.State);
        Assert.Null(coordinator.CurrentProfile);
        Assert.Empty(recovery.Profiles);
    }

    [Fact]
    public void Restore_stabilization_timeout_keeps_the_recovery_profile()
    {
        var api = new FakeWindowApi();
        var backend = new FakeVisibilityBackend();
        var recovery = new RecoveryManager(api, backend);
        var coordinator = new GhostCoordinator(api, recovery, new VisibilityEngine(backend));
        Assert.True(coordinator.SelectWindow(api.Target, new RevealSettings()));
        api.Observation = api.Observation with { ScreenBounds = new Rectangle(90, 80, 540, 430) };

        Assert.True(coordinator.RestoreCurrent("test"));
        for (var i = 0; i < RestoreStabilityTracker.MaximumCorrections + 1; i++)
        {
            coordinator.UpdatePeek(Point.Empty, false);
        }

        Assert.Equal(GhostState.RecoveryError, coordinator.State);
        Assert.NotNull(coordinator.CurrentProfile);
        Assert.Single(recovery.Profiles);
        Assert.Equal(11, backend.RestoreCalls);
    }

    private static void CompleteRestore(GhostCoordinator coordinator)
    {
        for (var i = 0; i < RestoreStabilityTracker.RequiredStableTicks; i++)
        {
            coordinator.UpdatePeek(Point.Empty, false);
        }
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
                IsMaximized = false,
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
            Placement = NormalPlacement(target.ScreenBounds),
            Styles = new WindowStyleSnapshot(0, 0),
            CapturedAt = DateTimeOffset.UtcNow,
            ProcessStartIdentity = "start"
        });

        private static WindowPlacementSnapshot NormalPlacement(Rectangle bounds) => new(
            0,
            1,
            Point.Empty,
            Point.Empty,
            bounds,
            Rectangle.Empty);

        public bool IsSameIdentity(WindowSnapshot snapshot, WindowObservation observation) =>
            snapshot.Hwnd == observation.Hwnd && snapshot.ProcessId == observation.ProcessId;
    }

    private sealed class FakeVisibilityBackend : IVisibilityBackend
    {
        private readonly List<string>? _calls;

        public FakeVisibilityBackend(List<string>? calls = null)
        {
            _calls = calls;
        }

        public int RevealCalls { get; private set; }
        public int GhostCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public int EnsurePlacementCalls { get; private set; }
        public CircleRegion? LastReveal { get; private set; }
        public WindowSnapshot? LastExpectedSnapshot { get; private set; }
        public NativeResult EnsurePlacementResult { get; init; } = NativeResult.Ok("EnsurePlacement");
        public Queue<NativeResult> RevealResults { get; } = new();
        public NativeResult ApplyGhost(nint hwnd, WindowSnapshot snapshot) { GhostCalls++; LastExpectedSnapshot = snapshot; return NativeResult.Ok("Ghost"); }
        public NativeResult ApplyReveal(nint hwnd, CircleRegion region, WindowSnapshot snapshot)
        {
            _calls?.Add("Reveal");
            RevealCalls++;
            LastReveal = region;
            LastExpectedSnapshot = snapshot;
            return RevealResults.TryDequeue(out var result) ? result : NativeResult.Ok("Reveal");
        }
        public NativeResult EnsureWindowPlacement(nint hwnd, WindowSnapshot snapshot) { EnsurePlacementCalls++; LastExpectedSnapshot = snapshot; return EnsurePlacementResult; }
        public NativeResult Restore(nint hwnd, WindowSnapshot snapshot) { RestoreCalls++; return NativeResult.Ok("Restore"); }
    }

    private sealed class FakeRevealVisualHost : IRevealVisualHost
    {
        private readonly List<string>? _calls;

        public FakeRevealVisualHost(List<string>? calls = null)
        {
            _calls = calls;
        }

        public bool IsAvailable { get; set; } = true;
        public NativeResult PrepareResult { get; init; } = NativeResult.Ok("Prepare");
        public NativeResult PresentResult { get; init; } = NativeResult.Ok("Present");
        public RevealVisualState? LastVisual { get; private set; }
        public int HideCalls { get; private set; }

        public NativeResult Prepare(RevealVisualState visual)
        {
            _calls?.Add("Prepare");
            LastVisual = visual;
            return PrepareResult;
        }

        public NativeResult Present()
        {
            _calls?.Add("Present");
            return PresentResult;
        }

        public void Hide()
        {
            HideCalls++;
        }
    }
}
