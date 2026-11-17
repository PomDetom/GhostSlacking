using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.Core.Tests;

public sealed class DisplayTransitionTests
{
    [Fact]
    public void Scaling_hidden_target_registers_only_new_geometry_and_round_trips_watchdog_before_reveal()
    {
        var h = new Harness();
        var original = h.Coordinator.CurrentProfile!.Original;
        var published = new List<RecoveryManifest>();
        h.Recovery.ProfilesChanged += (_, _) => published.Add(RecoveryManifest.FromProfiles(Guid.NewGuid(), h.Recovery.Profiles, DateTimeOffset.UtcNow));
        h.Scale(125);
        h.Tick(true);
        Assert.True(h.Coordinator.IsEnvironmentTransitioning);
        Assert.Equal(0, h.Backend.Corrections);
        Assert.Equal(0, h.Backend.Reveals);
        h.Time.Advance(250);
        h.Tick(true);
        var updated = h.Coordinator.CurrentProfile!.Original;
        Assert.Equal(original with { ScreenBounds = h.Api.Bounds, Placement = original.Placement with
        {
            NormalPosition = h.Api.Bounds, DevicePosition = h.Api.Bounds
        } }, updated);
        Assert.Equal(updated, h.Recovery.Profiles.Single().Original);
        var message = new WatchdogMessage
        {
            SessionId = Guid.NewGuid(), Kind = WatchdogMessageKind.RecoveryManifest,
            SentAtUtc = DateTimeOffset.UtcNow, Manifest = Assert.Single(published)
        };
        var roundTrip = WatchdogProtocol.Deserialize(WatchdogProtocol.Serialize(message)).Manifest!.Items.Single().ToSnapshot();
        Assert.Equal(updated.OriginalRegionData, roundTrip.OriginalRegionData);
        Assert.Equal(updated with { OriginalRegionData = roundTrip.OriginalRegionData }, roundTrip);
        Assert.Equal(1, h.Backend.Reveals);
        Assert.True(h.Coordinator.RestoreCurrent("test"));
        Assert.Equal(updated, h.Backend.LastRestored);
    }

    [Fact]
    public void Maximum_state_and_restore_flags_survive_hidden_show_command_and_reverse_scaling()
    {
        var h = new Harness(maximized: true);
        h.Scale(125);
        h.Tick();
        h.Time.Advance(250);
        h.Tick();
        Assert.Equal(3, h.Coordinator.CurrentProfile!.Original.Placement.ShowCommand);
        Assert.Equal(2, h.Coordinator.CurrentProfile.Original.Placement.Flags);
        h.Scale(100);
        h.Tick();
        h.Time.Advance(250);
        h.Tick();
        Assert.Equal(new Rectangle(100, 100, 500, 400), h.Coordinator.CurrentProfile!.Original.ScreenBounds);
        Assert.Equal(0, h.Backend.Corrections);
    }

    [Fact]
    public void Late_geometry_changes_restart_the_stability_window()
    {
        var h = new Harness();
        h.Scale(125, resize: false);
        h.Tick();
        h.Time.Advance(200);
        h.Api.Bounds = new Rectangle(100, 100, 625, 500);
        h.Tick();
        h.Time.Advance(100);
        h.Tick();
        Assert.True(h.Coordinator.IsEnvironmentTransitioning);
        Assert.Equal(0, h.Backend.Reveals);
        h.Time.Advance(150);
        h.Tick();
        Assert.False(h.Coordinator.IsEnvironmentTransitioning);
        Assert.Equal(h.Api.Bounds, h.Coordinator.CurrentProfile!.Original.ScreenBounds);
    }

    [Fact]
    public void Scale_is_detected_even_when_dpi_unaware_target_dpi_stays_96()
    {
        var h = new Harness();
        h.Scale(125);
        h.Api.Display = h.Api.Display with { WindowDpi = 96 };
        h.Tick();
        Assert.True(h.Coordinator.IsEnvironmentTransitioning);
        Assert.Equal(0, h.Backend.Corrections);
    }

    [Fact]
    public void Resize_after_first_quiet_interval_is_adopted_inside_the_original_timeout_budget()
    {
        var h = new Harness();
        h.Scale(125, resize: false);
        h.Tick();
        h.Time.Advance(250);
        h.Tick();
        Assert.False(h.Coordinator.IsEnvironmentTransitioning);
        h.Time.Advance(150);
        h.Api.Bounds = new Rectangle(100, 100, 625, 500);
        h.Tick();
        Assert.True(h.Coordinator.IsEnvironmentTransitioning);
        Assert.Equal(0, h.Backend.Corrections);
        h.Time.Advance(250);
        h.Tick();
        Assert.False(h.Coordinator.IsEnvironmentTransitioning);
        Assert.Equal(h.Api.Bounds, h.Coordinator.CurrentProfile!.Original.ScreenBounds);
    }

    [Fact]
    public void Unrelated_settings_broadcast_does_not_interrupt_peek()
    {
        var h = new Harness();
        var events = 0;
        h.Coordinator.EnvironmentChanging += (_, _) => events++;
        Assert.True(h.Coordinator.RefreshWindowEnvironment());
        Assert.True(h.Coordinator.RefreshWindowEnvironment());
        h.Tick(true);
        Assert.Equal(0, events);
        Assert.Equal(GhostState.Reveal, h.Coordinator.State);
    }

    [Fact]
    public void Work_area_change_rebuilds_geometry_without_old_placement_correction()
    {
        var h = new Harness();
        h.Api.Display = h.Api.Display with { WorkArea = new Rectangle(0, 40, 1920, 1040) };
        h.Tick();
        Assert.True(h.Coordinator.IsEnvironmentTransitioning);
        h.Time.Advance(250);
        h.Tick();
        Assert.False(h.Coordinator.IsEnvironmentTransitioning);
        Assert.Equal(0, h.Backend.Corrections);
    }

    [Fact]
    public void Unstable_scaling_times_out_hidden_and_notifies_only_once_without_restoring()
    {
        var h = new Harness();
        var errors = 0;
        h.Coordinator.UserErrorOccurred += (_, _) => errors++;
        h.Scale(125);
        h.Tick(true);
        for (var i = 0; i < 9; i++)
        {
            h.Api.Bounds = new Rectangle(100, 100, 620 + i, 500);
            h.Time.Advance(250);
            h.Tick(true);
        }
        Assert.Equal(GhostState.RecoveryError, h.Coordinator.State);
        Assert.False(h.Api.Visible);
        Assert.Equal(1, errors);
        Assert.Equal(0, h.Backend.Corrections);
        Assert.Equal(0, h.Backend.Reveals);
        Assert.Null(h.Backend.LastRestored);
        Assert.Single(h.Recovery.Profiles);
    }

    [Fact]
    public void Query_failure_stops_hidden_with_recovery_data_and_no_repeat_attempts()
    {
        var h = new Harness();
        h.Tick(true);
        h.Api.FailQuery = true;
        h.Tick(true);
        var hides = h.Backend.Hides;
        h.Tick(true);
        Assert.Equal(GhostState.RecoveryError, h.Coordinator.State);
        Assert.False(h.Api.Visible);
        Assert.Equal(hides, h.Backend.Hides);
        Assert.Single(h.Recovery.Profiles);
    }

    [Fact]
    public void Failed_initial_display_capture_leaves_target_untouched()
    {
        var h = new Harness(select: false);
        h.Api.FailQuery = true;
        Assert.False(h.Coordinator.SelectWindow(h.Api.Target, new RevealSettings()));
        Assert.True(h.Api.Visible);
        Assert.Equal(0, h.Backend.Hides);
        Assert.Empty(h.Recovery.Profiles);
    }

    [Fact]
    public void Geometry_registration_rejects_reused_identity_and_changed_bounds()
    {
        var h = new Harness();
        var environment = h.Api.ReadEnvironment(h.Api.Snapshot).Value!;
        h.Api.IdentityMatches = false;
        Assert.False(h.Recovery.UpdateGeometry(h.Api.Target.Hwnd, environment).Success);
        h.Api.IdentityMatches = true;
        h.Api.Bounds = new Rectangle(0, 0, 50, 50);
        Assert.False(h.Recovery.UpdateGeometry(h.Api.Target.Hwnd, environment).Success);
        Assert.Equal(h.Api.Snapshot, h.Recovery.Profiles.Single().Original);
    }

    [Fact]
    public void Target_closing_or_changing_identity_during_scaling_never_receives_more_native_writes()
    {
        var h = new Harness();
        h.Scale(125);
        h.Tick();
        var hides = h.Backend.Hides;
        h.Api.IdentityMatches = false;
        h.Time.Advance(250);
        h.Tick(true);
        Assert.Equal(GhostState.Idle, h.Coordinator.State);
        Assert.Equal(hides, h.Backend.Hides);
        Assert.Equal(0, h.Backend.Reveals);
        Assert.Empty(h.Recovery.Profiles);
    }

    [Fact]
    public void Ordinary_drift_correction_is_bounded_without_reclassifying_it_as_scaling()
    {
        var h = new Harness();
        h.Api.Bounds = new Rectangle(100, 100, 700, 500);
        for (var i = 0; i < 15; i++) h.Tick(true);
        Assert.Equal(10, h.Backend.Corrections);
        Assert.Equal(GhostState.RecoveryError, h.Coordinator.State);
        Assert.False(h.Api.Visible);
        Assert.Equal(0, h.Backend.Reveals);
    }

    [Fact]
    public void Visible_target_adapts_without_being_hidden_or_revealed()
    {
        var h = new Harness();
        Assert.True(h.Coordinator.ToggleWindowVisibility("test"));
        for (var i = 0; i < 3; i++) h.Tick();
        Assert.Equal(GhostState.Visible, h.Coordinator.State);
        var hides = h.Backend.Hides;
        h.Scale(125);
        h.Tick();
        h.Time.Advance(250);
        h.Tick();
        Assert.Equal(hides, h.Backend.Hides);
        Assert.True(h.Api.Visible);
        Assert.Equal(GhostState.Visible, h.Coordinator.State);
        Assert.Equal(h.Api.Bounds, h.Coordinator.CurrentProfile!.Original.ScreenBounds);
    }

    [Fact]
    public void Scaling_invalidates_overlay_once_and_preserves_live_reveal_settings()
    {
        var h = new Harness();
        h.Tick(true);
        Assert.Equal(GhostState.Reveal, h.Coordinator.State);
        var settings = new RevealSettings { DiameterPx = 320, SoftEdgeWidthPx = 32 };
        Assert.True(h.Coordinator.UpdateRevealSettings(settings));
        var events = 0;
        h.Coordinator.EnvironmentChanging += (_, _) => events++;
        h.Scale(125);
        h.Tick(true);
        h.Tick(true);
        Assert.Equal(GhostState.Ghost, h.Coordinator.State);
        Assert.False(h.Api.Visible);
        Assert.Equal(1, events);
        Assert.Equal(1, h.Visual.Invalidations);
        h.Time.Advance(250);
        h.Tick(true);
        Assert.Equal(settings, h.Coordinator.CurrentProfile!.Reveal);
        Assert.Equal(GhostState.Reveal, h.Coordinator.State);
        Assert.Equal(h.Api.Bounds, h.Visual.LastVisual!.WindowBounds);
        Assert.Equal(320, h.Visual.LastVisual.CoreRegion.DiameterPx);
    }

    [Fact]
    public void Restore_all_from_recovery_error_adopts_valid_system_maximized_bounds_and_forgets_target()
    {
        var h = new Harness(maximized: true);
        h.Api.FailQuery = true;
        h.Tick();
        Assert.Equal(GhostState.RecoveryError, h.Coordinator.State);
        h.Api.FailQuery = false;
        h.Backend.RestoredBounds = new Rectangle(-8, -8, 1936, 1048);
        var report = h.Coordinator.RestoreAll("100 percent emergency restore");
        Assert.True(report.Success);
        Assert.True(h.Api.Visible);
        Assert.Equal(GhostState.Idle, h.Coordinator.State);
        Assert.Null(h.Coordinator.CurrentProfile);
        Assert.Empty(h.Recovery.Profiles);
        Assert.Equal(1, h.Backend.Restores);
    }

    [Fact]
    public void Async_restore_uses_registered_new_maximized_bounds_for_three_stable_observations()
    {
        var h = new Harness(maximized: true);
        var initial = h.Coordinator.CurrentProfile!.Original;
        h.Backend.RestoredBounds = new Rectangle(-8, -8, 1936, 1048);
        Assert.True(h.Coordinator.RestoreCurrent("100 percent restore"));
        var updated = h.Coordinator.CurrentProfile!.Original;
        Assert.Equal(h.Backend.RestoredBounds, updated.ScreenBounds);
        Assert.Equal(initial.Styles, updated.Styles);
        Assert.Equal(initial.ProcessStartIdentity, updated.ProcessStartIdentity);
        Assert.Equal(initial.OriginalRegionData, updated.OriginalRegionData);
        h.Tick();
        h.Tick();
        Assert.Equal(GhostState.Restoring, h.Coordinator.State);
        h.Tick();
        Assert.Equal(GhostState.Idle, h.Coordinator.State);
        Assert.Empty(h.Recovery.Profiles);
        Assert.Equal(1, h.Backend.Restores);
    }

    [Fact]
    public void Visible_maximized_window_with_invalid_system_bounds_is_not_forgotten()
    {
        var h = new Harness(maximized: true);
        h.Api.ValidMaximizedBounds = false;
        h.Backend.RestoredBounds = new Rectangle(0, 0, 400, 300);
        Assert.False(h.Coordinator.RestoreAll("invalid max geometry").Success);
        Assert.Equal(GhostState.RecoveryError, h.Coordinator.State);
        Assert.Single(h.Recovery.Profiles);
        Assert.Equal(h.Api.Snapshot.ScreenBounds, h.Recovery.Profiles.Single().Original.ScreenBounds);
    }

    [Fact]
    public void Normal_window_restore_keeps_exact_geometry_requirement()
    {
        var h = new Harness();
        h.Backend.RestoredBounds = new Rectangle(0, 0, 400, 300);
        Assert.False(h.Coordinator.RestoreAll("normal geometry mismatch").Success);
        Assert.Single(h.Recovery.Profiles);
    }

    [Fact]
    public void Identity_changed_during_restore_geometry_registration_is_rejected()
    {
        var h = new Harness(maximized: true);
        h.Backend.RestoredBounds = new Rectangle(-8, -8, 1936, 1048);
        h.Backend.ReplaceIdentityOnRestore = true;
        Assert.False(h.Coordinator.RestoreCurrent("identity race"));
        Assert.Equal(GhostState.RecoveryError, h.Coordinator.State);
        Assert.Equal(h.Api.Snapshot, h.Recovery.Profiles.Single().Original);
    }

    [Fact]
    public void Temporary_visible_profile_keeps_geometry_updated_during_restore_stabilization()
    {
        var h = new Harness(maximized: true);
        h.Backend.RestoredBounds = new Rectangle(-8, -8, 1936, 1048);
        Assert.True(h.Coordinator.ToggleWindowVisibility("show at 100 percent"));
        var settled = new Rectangle(-9, -9, 1938, 1038);
        h.Api.Bounds = settled;
        h.Backend.RestoredBounds = settled;
        h.Tick();
        h.Tick();
        h.Tick();
        h.Tick();
        Assert.Equal(GhostState.Visible, h.Coordinator.State);
        Assert.Equal(settled, h.Coordinator.CurrentProfile!.Original.ScreenBounds);
        Assert.Equal(settled, h.Recovery.Profiles.Single().Original.ScreenBounds);
    }

    private sealed class Harness
    {
        public Api Api { get; }
        public Backend Backend { get; }
        public RecoveryManager Recovery { get; }
        public GhostCoordinator Coordinator { get; }
        public ManualTime Time { get; } = new();
        public Visual Visual { get; } = new();
        public Harness(bool maximized = false, bool select = true)
        {
            Api = new Api(maximized);
            Backend = new Backend(Api);
            Recovery = new RecoveryManager(Api, Backend);
            Coordinator = new GhostCoordinator(Api, Recovery, new VisibilityEngine(Backend), revealVisualHost: Visual, timeProvider: Time);
            if (select) Assert.True(Coordinator.SelectWindow(Api.Target, new RevealSettings()));
        }
        public void Tick(bool peek = false) => Coordinator.UpdatePeek(new Point(200, 200), peek);
        public void Scale(int scale, bool resize = true)
        {
            Api.Display = Api.Display with { WindowDpi = (uint)(96 * scale / 100), ScalePercent = scale };
            if (resize) Api.Bounds = new Rectangle(100, 100, 500 * scale / 100, 400 * scale / 100);
        }
    }

    private sealed class Visual : IRevealVisualHost
    {
        public bool IsAvailable => true;
        public int Invalidations { get; private set; }
        public RevealVisualState? LastVisual { get; private set; }
        public NativeResult Prepare(RevealVisualState visual) { LastVisual = visual; return NativeResult.Ok("Prepare"); }
        public NativeResult Present() => NativeResult.Ok("Present");
        public void Hide() { }
        public void InvalidateGeometry() { Invalidations++; LastVisual = null; }
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _timestamp;
        public void Advance(long milliseconds) => _timestamp += milliseconds;
    }

    private sealed class Api : IWindowApi, IWindowEnvironmentApi
    {
        public Rectangle Bounds { get; set; } = new(100, 100, 500, 400);
        public WindowDisplayContext Display { get; set; } = new("monitor", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 96, 100);
        public bool Visible { get; set; } = true;
        public bool IdentityMatches { get; set; } = true;
        public bool FailQuery { get; set; }
        public bool ValidMaximizedBounds { get; set; } = true;
        public bool Maximized { get; }
        public TargetWindow Target { get; }
        public WindowSnapshot Snapshot { get; }
        public Api(bool maximized)
        {
            Maximized = maximized;
            Target = new TargetWindow { Hwnd = 42, ProcessId = 100, ScreenBounds = Bounds };
            Snapshot = new WindowSnapshot
            {
                Hwnd = 42, ProcessId = 100, ProcessStartIdentity = "original-start", ScreenBounds = Bounds,
                WasVisible = true, WasMinimized = false,
                Placement = new(2, maximized ? 3 : 1, new(-1, -1), new(0, 0), Bounds, Bounds),
                Styles = new(1234, 5678), CapturedAt = DateTimeOffset.UtcNow,
                OriginalRegionData = [1, 2, 3], OriginalBorderColor = 123, OriginalSystemBackdropType = 2,
                OriginalWindowCornerPreference = 1, OriginalNonClientRenderingPolicy = 0
            };
        }
        public bool IsWindow(nint hwnd) => true;
        public WindowObservation? Observe(nint hwnd) => new()
        {
            Hwnd = hwnd, ProcessId = 100, ScreenBounds = Bounds, IsVisible = Visible,
            IsMinimized = false, IsMaximized = Maximized
        };
        public bool IsSameIdentity(WindowSnapshot snapshot, WindowObservation observation) => IdentityMatches;
        public OperationResult<WindowSnapshot> CaptureSnapshot(TargetWindow target) => OperationResult<WindowSnapshot>.Ok(Snapshot);
        public Point GetCursorPosition() => Point.Empty;
        public OperationResult<WindowEnvironment> ReadEnvironment(WindowSnapshot original) => FailQuery
            ? OperationResult<WindowEnvironment>.Failed("query failed")
            : OperationResult<WindowEnvironment>.Ok(new(Display, Bounds, original.Placement with
            {
                NormalPosition = Bounds, DevicePosition = Bounds, ShowCommand = Visible ? original.Placement.ShowCommand : 0, Flags = 0
            }) { IsSystemMaximized = Maximized && ValidMaximizedBounds });
    }

    private sealed class Backend(Api api) : IVisibilityBackend
    {
        public int Corrections { get; private set; }
        public int Reveals { get; private set; }
        public int Hides { get; private set; }
        public int Restores { get; private set; }
        public Rectangle? RestoredBounds { get; set; }
        public bool ReplaceIdentityOnRestore { get; set; }
        public WindowSnapshot? LastRestored { get; private set; }
        public NativeResult HideOnly(nint hwnd) { Hides++; api.Visible = false; return NativeResult.Ok("Hide"); }
        public NativeResult ApplyGhost(nint hwnd, WindowSnapshot snapshot) => HideOnly(hwnd);
        public NativeResult ApplyReveal(nint hwnd, CircleRegion region, WindowSnapshot snapshot) { Reveals++; api.Visible = true; return NativeResult.Ok("Reveal"); }
        public NativeResult UpdateRevealRegion(nint hwnd, CircleRegion region) => NativeResult.Ok("Update");
        public NativeResult EnsureWindowPlacement(nint hwnd, WindowSnapshot snapshot) { Corrections++; return NativeResult.Ok("Correct"); }
        public NativeResult Restore(nint hwnd, WindowSnapshot snapshot)
        {
            Restores++;
            LastRestored = snapshot;
            api.Visible = snapshot.WasVisible;
            api.Bounds = RestoredBounds ?? snapshot.ScreenBounds;
            if (ReplaceIdentityOnRestore) api.IdentityMatches = false;
            return NativeResult.Ok("Restore");
        }
    }
}
