using System.Drawing;

namespace GhostSlacking.App.Tests;

public sealed class RevealPerformanceTrackerTests
{
    [Fact]
    public void Moving_samples_emit_one_rate_limited_summary()
    {
        var tracker = new RevealPerformanceTracker();
        RevealPerformanceSummary? summary = null;

        for (var index = 0; index < RevealPerformanceTracker.SampleWindowSize; index++)
        {
            var duration = index >= 113 ? TimeSpan.FromMilliseconds(20) : TimeSpan.FromMilliseconds(1);
            summary = tracker.Record(
                new Point(index, 0),
                duration,
                TimeSpan.FromSeconds(index / 60D),
                60);
        }

        Assert.NotNull(summary);
        Assert.Equal(120, summary.FrameCount);
        Assert.Equal(60, summary.TargetFramesPerSecond);
        Assert.Equal(60D, summary.EffectiveFramesPerSecond, 3);
        Assert.Equal(TimeSpan.FromMilliseconds(20), summary.P95Duration);
        Assert.Equal(TimeSpan.FromMilliseconds(20), summary.MaximumDuration);
        Assert.Equal(7, summary.OverBudgetCount);
    }

    [Fact]
    public void Stationary_cursor_does_not_fill_the_sample_window()
    {
        var tracker = new RevealPerformanceTracker();
        RevealPerformanceSummary? summary = null;

        for (var index = 0; index < RevealPerformanceTracker.SampleWindowSize * 2; index++)
        {
            summary = tracker.Record(
                new Point(10, 10),
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromSeconds(index / 60D),
                60);
        }

        Assert.Null(summary);
    }

    [Fact]
    public void Reset_starts_a_fresh_sample_window()
    {
        var tracker = new RevealPerformanceTracker();
        for (var index = 0; index < RevealPerformanceTracker.SampleWindowSize - 1; index++)
        {
            tracker.Record(
                new Point(index, 0),
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromSeconds(index / 60D),
                60);
        }

        tracker.Reset();

        Assert.Null(tracker.Record(
            new Point(500, 0),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromSeconds(5),
            60));
    }

    [Fact]
    public void Target_change_resets_samples_and_uses_the_new_frame_budget()
    {
        var tracker = new RevealPerformanceTracker();
        for (var index = 0; index < RevealPerformanceTracker.SampleWindowSize - 1; index++)
        {
            tracker.Record(
                new Point(index, 0),
                TimeSpan.FromMilliseconds(9),
                TimeSpan.FromSeconds(index / 60D),
                60);
        }

        Assert.Null(tracker.Record(new Point(500, 0), TimeSpan.FromMilliseconds(9), TimeSpan.Zero, 120));

        RevealPerformanceSummary? summary = null;
        for (var index = 1; index < RevealPerformanceTracker.SampleWindowSize; index++)
        {
            summary = tracker.Record(
                new Point(500 + index, 0),
                TimeSpan.FromMilliseconds(9),
                TimeSpan.FromSeconds(index / 120D),
                120);
        }

        Assert.NotNull(summary);
        Assert.Equal(120, summary.TargetFramesPerSecond);
        Assert.Equal(RevealPerformanceTracker.SampleWindowSize, summary.OverBudgetCount);
    }
}
