using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.Core.Tests;

public sealed class WindowPlacementRestorationTests
{
    [Fact]
    public void Normal_and_snap_windows_require_the_original_screen_bounds()
    {
        var snapshot = Snapshot(new Rectangle(0, 0, 960, 1040), showCommand: 1);
        var matching = Observation(snapshot.ScreenBounds);
        var drifted = matching with { ScreenBounds = new Rectangle(0, 0, 1000, 1040) };

        Assert.True(WindowPlacementRestoration.Matches(snapshot, matching));
        Assert.False(WindowPlacementRestoration.Matches(snapshot, drifted));
    }

    [Fact]
    public void Maximized_windows_require_maximized_state_and_original_device_bounds()
    {
        var snapshot = Snapshot(new Rectangle(-1920, 0, 1920, 1080), showCommand: 3);
        var matching = Observation(snapshot.ScreenBounds) with { IsMaximized = true };
        var normalized = matching with { IsMaximized = false };

        Assert.True(WindowPlacementRestoration.Matches(snapshot, matching));
        Assert.False(WindowPlacementRestoration.Matches(snapshot, normalized));
    }

    [Fact]
    public void Minimized_windows_use_the_placement_state_instead_of_transient_icon_bounds()
    {
        var snapshot = Snapshot(new Rectangle(100, 100, 800, 600), showCommand: 2);
        var minimized = Observation(new Rectangle(-32000, -32000, 160, 28)) with
        {
            IsMinimized = true
        };

        Assert.True(WindowPlacementRestoration.Matches(snapshot, minimized));
        Assert.False(WindowPlacementRestoration.Matches(snapshot, minimized with { IsMinimized = false }));
    }

    [Fact]
    public void Hidden_window_restore_requires_original_visibility()
    {
        var snapshot = Snapshot(new Rectangle(100, 100, 800, 600), showCommand: 1) with
        {
            WasVisible = false
        };

        Assert.True(WindowPlacementRestoration.Matches(snapshot, Observation(snapshot.ScreenBounds) with { IsVisible = false }));
        Assert.False(WindowPlacementRestoration.Matches(snapshot, Observation(snapshot.ScreenBounds)));
    }

    [Fact]
    public void Stability_tracker_resets_on_drift_and_limits_corrections()
    {
        var tracker = new RestoreStabilityTracker();
        tracker.ObserveMatch();
        tracker.ObserveMatch();
        Assert.False(tracker.IsStable);

        tracker.ObserveDrift();
        Assert.Equal(0, tracker.StableTicks);

        for (var i = 0; i < RestoreStabilityTracker.MaximumCorrections; i++)
        {
            tracker.RecordCorrection();
        }

        Assert.False(tracker.CanCorrect);
        Assert.Throws<InvalidOperationException>(tracker.RecordCorrection);
    }

    private static WindowSnapshot Snapshot(Rectangle bounds, int showCommand) => new()
    {
        Hwnd = 42,
        ProcessId = 100,
        ProcessStartIdentity = "start",
        ScreenBounds = bounds,
        WasVisible = true,
        WasMinimized = showCommand == 2,
        Placement = new WindowPlacementSnapshot(
            0,
            showCommand,
            Point.Empty,
            Point.Empty,
            new Rectangle(100, 100, 800, 600),
            new Rectangle(-1920, 0, 1920, 1080)),
        Styles = new WindowStyleSnapshot(0, 0),
        CapturedAt = DateTimeOffset.UtcNow
    };

    private static WindowObservation Observation(Rectangle bounds) => new()
    {
        Hwnd = 42,
        ProcessId = 100,
        ScreenBounds = bounds,
        IsVisible = true,
        IsMinimized = false,
        IsMaximized = false
    };
}
