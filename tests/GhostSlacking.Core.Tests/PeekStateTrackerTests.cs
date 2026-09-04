using GhostSlacking.Core;

namespace GhostSlacking.Core.Tests;

public sealed class PeekStateTrackerTests
{
    [Fact]
    public void Toggle_changes_only_on_the_key_down_edge()
    {
        var tracker = new PeekStateTracker();

        Assert.False(tracker.Update(false, PeekTrigger.Toggle));
        Assert.True(tracker.Update(true, PeekTrigger.Toggle));
        Assert.True(tracker.Update(true, PeekTrigger.Toggle));
        Assert.True(tracker.Update(false, PeekTrigger.Toggle));
        Assert.False(tracker.Update(true, PeekTrigger.Toggle));
    }

    [Fact]
    public void Hold_follows_the_physical_key_state()
    {
        var tracker = new PeekStateTracker();

        Assert.False(tracker.Update(false, PeekTrigger.Hold));
        Assert.True(tracker.Update(true, PeekTrigger.Hold));
        Assert.False(tracker.Update(false, PeekTrigger.Hold));
    }
}
