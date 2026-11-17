using GhostSlacking.App;

namespace GhostSlacking.App.Tests;

public sealed class TrayDoubleClickDetectorTests
{
    [Fact]
    public void Second_click_inside_interval_is_a_double_click()
    {
        var detector = new TrayDoubleClickDetector(TimeSpan.FromMilliseconds(500));
        var now = DateTimeOffset.UtcNow;

        Assert.False(detector.RegisterClick(now));
        Assert.True(detector.RegisterClick(now.AddMilliseconds(400)));
        Assert.False(detector.RegisterClick(now.AddMilliseconds(450)));
    }

    [Fact]
    public void Click_after_interval_starts_a_new_sequence()
    {
        var detector = new TrayDoubleClickDetector(TimeSpan.FromMilliseconds(500));
        var now = DateTimeOffset.UtcNow;

        Assert.False(detector.RegisterClick(now));
        Assert.False(detector.RegisterClick(now.AddMilliseconds(501)));
    }

    [Fact]
    public void Backward_timestamp_does_not_form_a_double_click()
    {
        var detector = new TrayDoubleClickDetector(TimeSpan.FromMilliseconds(500));
        var now = DateTimeOffset.UtcNow;

        Assert.False(detector.RegisterClick(now));
        Assert.False(detector.RegisterClick(now.AddMilliseconds(-1)));
    }
}
