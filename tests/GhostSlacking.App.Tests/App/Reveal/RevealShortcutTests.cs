using GhostSlacking.App;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

public sealed class RevealShortcutTests
{
    [Theory]
    [InlineData(240, 1, true, (int)DiameterAdjustmentResult.Updated, 256)]
    [InlineData(240, -1, true, (int)DiameterAdjustmentResult.Updated, 224)]
    [InlineData(800, 1, true, (int)DiameterAdjustmentResult.Unchanged, 800)]
    [InlineData(64, -1, true, (int)DiameterAdjustmentResult.Unchanged, 64)]
    [InlineData(240, 1, false, (int)DiameterAdjustmentResult.SaveFailed, 240)]
    public void Diameter_adjustment_keeps_peek_and_commits_only_after_save(
        int diameter, int direction, bool saveSucceeds, int expectedValue, int expectedDiameter)
    {
        var peek = new PeekStateTracker();
        var expected = (DiameterAdjustmentResult)expectedValue;
        Assert.True(peek.Update(true, PeekTrigger.Toggle));
        Assert.True(peek.Update(false, PeekTrigger.Toggle));
        var settings = new AppSettings { RevealDiameterPx = diameter };
        var saves = 0;
        var result = DiameterAdjustmentResult.Unchanged;
        var shortcut = direction > 0 ? GhostApplicationController.DiameterIncreaseHotkey : GhostApplicationController.DiameterDecreaseHotkey;
        GhostApplicationController.DispatchHotkey(shortcut, () => peek.Reset(), _ =>
            result = GhostApplicationController.TryAdjustRevealDiameter(settings, direction, _ => { saves++; return saveSucceeds; }, out settings));

        Assert.Equal(expected, result);
        Assert.Equal(expectedDiameter, settings.RevealDiameterPx);
        Assert.Equal(expected == DiameterAdjustmentResult.Unchanged ? 0 : 1, saves);
        Assert.True(peek.Update(false, PeekTrigger.Toggle));
    }

    [Theory]
    [InlineData(GhostApplicationController.DiameterIncreaseHotkey)]
    [InlineData(GhostApplicationController.DiameterDecreaseHotkey)]
    public void Diameter_shortcuts_dispatch_without_cancelling_reveal(int shortcut)
    {
        var cancelled = false;
        var dispatched = 0;
        GhostApplicationController.DispatchHotkey(shortcut, () => cancelled = true, id => dispatched = id);

        Assert.False(cancelled);
        Assert.Equal(shortcut, dispatched);
        Assert.True(GhostApplicationController.PreservesReveal(shortcut));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Other_global_shortcuts_still_cancel_before_dispatch(int shortcut)
    {
        var events = new List<string>();
        GhostApplicationController.DispatchHotkey(shortcut, () => events.Add("cancel"), _ => events.Add("dispatch"));
        Assert.Equal(new[] { "cancel", "dispatch" }, events);
        Assert.False(GhostApplicationController.PreservesReveal(shortcut));
    }
}
