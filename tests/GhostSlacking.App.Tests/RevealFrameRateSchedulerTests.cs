using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App.Tests;

public sealed class RevealFrameRateSchedulerTests
{
    [Theory]
    [InlineData(60, 60)]
    [InlineData(75, 75)]
    [InlineData(120, 120)]
    [InlineData(144, 120)]
    [InlineData(165, 120)]
    [InlineData(240, 120)]
    public void Auto_follows_the_display_up_to_120_fps(int detected, int expected)
    {
        var decision = RevealFrameRatePolicy.Create(
            true,
            PeekFrameRateLimit.Auto,
            new DisplayRefreshRateInfo("display", detected));

        Assert.Equal(expected, decision.TargetFramesPerSecond);
        Assert.False(decision.UsesFallback);
    }

    [Theory]
    [InlineData(PeekFrameRateLimit.Fps60, 144, 60)]
    [InlineData(PeekFrameRateLimit.Fps90, 144, 90)]
    [InlineData(PeekFrameRateLimit.Fps90, 75, 75)]
    [InlineData(PeekFrameRateLimit.Fps120, 144, 120)]
    public void Explicit_limit_is_an_upper_bound(
        PeekFrameRateLimit limit,
        int detected,
        int expected)
    {
        var decision = RevealFrameRatePolicy.Create(
            true,
            limit,
            new DisplayRefreshRateInfo("display", detected));

        Assert.Equal(expected, decision.TargetFramesPerSecond);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(19)]
    [InlineData(501)]
    public void Invalid_detected_rate_falls_back_to_60_fps(int detected)
    {
        var decision = RevealFrameRatePolicy.Create(
            true,
            PeekFrameRateLimit.Auto,
            new DisplayRefreshRateInfo("display", detected));

        Assert.Equal(60, decision.TargetFramesPerSecond);
        Assert.True(decision.UsesFallback);
        Assert.Null(decision.DetectedRefreshRate);
        Assert.Null(decision.DisplayId);
    }

    [Fact]
    public void Missing_display_falls_back_to_60_fps()
    {
        var decision = RevealFrameRatePolicy.Create(true, PeekFrameRateLimit.Auto, null);

        Assert.Equal(60, decision.TargetFramesPerSecond);
        Assert.True(decision.UsesFallback);
    }

    [Fact]
    public void Inactive_polling_remains_at_60_fps()
    {
        var decision = RevealFrameRatePolicy.Create(
            false,
            PeekFrameRateLimit.Fps120,
            new DisplayRefreshRateInfo("display", 240));

        Assert.Equal(60, decision.TargetFramesPerSecond);
        Assert.False(decision.UsesFallback);
        Assert.Null(decision.DisplayId);
    }

    [Fact]
    public void Scheduler_changes_interval_only_when_the_effective_rate_changes()
    {
        var scheduler = new RevealFrameRateScheduler();

        var first = scheduler.Update(
            true,
            PeekFrameRateLimit.Auto,
            new DisplayRefreshRateInfo("display-a", 120));
        var same = scheduler.Update(
            true,
            PeekFrameRateLimit.Auto,
            new DisplayRefreshRateInfo("display-a", 120));
        var otherDisplay = scheduler.Update(
            true,
            PeekFrameRateLimit.Auto,
            new DisplayRefreshRateInfo("display-b", 120));

        Assert.True(first.ContextChanged);
        Assert.True(first.IntervalChanged);
        Assert.False(same.ContextChanged);
        Assert.False(same.IntervalChanged);
        Assert.True(otherDisplay.ContextChanged);
        Assert.False(otherDisplay.IntervalChanged);
    }
}
