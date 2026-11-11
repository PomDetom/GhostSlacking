using System.Drawing;
using GhostSlacking.Platform;

namespace GhostSlacking.App.Tests;

public sealed class BackgroundSemanticClickTests
{
    [Fact]
    public void A_complete_click_can_commit_only_once()
    {
        var click = new BackgroundSemanticClick();
        click.Begin(7, new(100, 100), 4);
        Assert.True(click.End(7, new(102, 101)));
        Assert.False(click.End(7, new(102, 101)));
    }

    [Fact]
    public void A_drag_that_returns_to_its_start_does_not_invoke()
    {
        var click = new BackgroundSemanticClick();
        click.Begin(7, new(100, 100), 4);
        click.Move(7, new(105, 100));
        click.Move(7, new(100, 100));
        Assert.False(click.End(7, new(100, 100)));
    }

    [Fact]
    public void Cancellation_or_a_different_gesture_cannot_commit_a_click()
    {
        var click = new BackgroundSemanticClick();
        click.Begin(7, Point.Empty, 4);
        Assert.False(click.End(8, Point.Empty));
        click.Begin(9, Point.Empty, 4);
        click.Cancel();
        Assert.False(click.End(9, Point.Empty));
    }

    [Fact]
    public void Extreme_monitor_coordinates_do_not_overflow_drag_checks()
    {
        var click = new BackgroundSemanticClick();
        click.Begin(7, new(int.MinValue, 0), 4);
        Assert.False(click.End(7, new(int.MaxValue, 0)));
    }
}
