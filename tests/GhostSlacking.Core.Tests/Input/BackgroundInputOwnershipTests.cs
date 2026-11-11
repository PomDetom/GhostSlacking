using GhostSlacking.Core;
namespace GhostSlacking.Core.Tests;

public sealed class BackgroundInputOwnershipTests
{
    private static BackgroundKeyInput Key(int key, bool down) => new(key, 0, down, false, false);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shortcut_key_and_release_are_consumed_while_modifier_ownership_is_preserved(bool active)
    {
        var input = new BackgroundInputOwnership(); input.Begin([], []);
        if (active) Assert.True(input.Activate());
        var modifierRoute = active ? BackgroundInputRoute.Deliver : BackgroundInputRoute.Consume;
        foreach (var modifier in new[] { 0xA2, 0xA4, 0xA0 })
            Assert.Equal(modifierRoute, input.Key(Key(modifier, true)));

        input.ConsumeKey(0x26);
        Assert.Equal(BackgroundInputRoute.Consume, input.Key(Key(0x26, true)));
        Assert.Equal(BackgroundInputRoute.Consume, input.Key(Key(0x26, false)));
        foreach (var modifier in new[] { 0xA0, 0xA4, 0xA2 })
            Assert.Equal(modifierRoute, input.Key(Key(modifier, false)));

        Assert.Equal(0, input.PendingCount);
        if (!active) Assert.True(input.Activate());
        Assert.Equal(BackgroundInputRoute.Deliver, input.Key(Key(0x41, true)));
        Assert.Equal(BackgroundInputRoute.Deliver, input.Key(Key(0x41, false)));
    }

    [Fact]
    public void Suppressed_shortcut_release_is_consumed_during_drain()
    {
        var input = new BackgroundInputOwnership(); input.Begin([], []); input.Activate();
        input.ConsumeKey(0x28);
        input.BeginDrain(DateTimeOffset.UnixEpoch);
        Assert.Equal(BackgroundInputRoute.Consume, input.Key(Key(0x28, false)));
        Assert.True(input.DrainFinished(DateTimeOffset.UnixEpoch, out var expired));
        Assert.False(expired);
    }
    [Fact]
    public void Foreground_owned_releases_pass_through_even_when_other_input_is_gated()
    {
        var input = new BackgroundInputOwnership();
        input.Begin([0xA0], [BackgroundMouseButton.Left]);
        Assert.Equal(BackgroundInputRoute.Consume, input.Key(Key(0x41, true)));
        Assert.Equal(BackgroundInputRoute.Foreground, input.Key(Key(0xA0, false)));
        Assert.Equal(BackgroundInputRoute.Foreground, input.Mouse(BackgroundMouseButton.Left, true, false));
        Assert.Equal(BackgroundInputRoute.Consume, input.Key(Key(0x41, false)));
        Assert.Equal(0, input.PendingCount);
    }
    [Fact]
    public void Preparing_events_are_consumed_without_becoming_delivery_work()
    {
        var input = new BackgroundInputOwnership(); input.Begin([], []);
        Assert.Equal(BackgroundInputRoute.Consume, input.Mouse(null, false, false));
        Assert.Equal(BackgroundInputRoute.Consume, input.Key(Key(0x41, true)));
        Assert.Equal(BackgroundInputRoute.Consume, input.Key(Key(0x41, false)));
        input.Activate();
        Assert.Equal(BackgroundInputRoute.Deliver, input.Key(Key(0x42, true)));
        Assert.Equal(BackgroundInputRoute.Deliver, input.Key(Key(0x42, false)));
    }
    [Fact]
    public void Preparation_cannot_promote_a_still_held_consumed_key_into_active_input()
    {
        var input = new BackgroundInputOwnership(); input.Begin([], []);
        input.Key(Key(0x41, true));
        Assert.False(input.Activate());
        Assert.Equal(BackgroundInputRoute.Consume, input.Key(Key(0x41, true)));
        Assert.Equal(BackgroundInputRoute.Consume, input.Key(Key(0x41, false)));
        Assert.True(input.Activate());
        input.BeginDrain(DateTimeOffset.UnixEpoch);
        Assert.False(input.Activate());
    }
    [Fact]
    public void An_unreleased_letter_does_not_capture_a_new_shift_tap_after_stop()
    {
        var input = new BackgroundInputOwnership(); input.Begin([], []); input.Activate();
        input.Key(Key(0x41, true)); input.BeginDrain(DateTimeOffset.UnixEpoch);
        Assert.Equal(BackgroundInputRoute.Foreground, input.Key(Key(0xA0, true)));
        Assert.Equal(BackgroundInputRoute.Foreground, input.Key(Key(0xA0, false)));
        Assert.Equal(BackgroundInputRoute.Consume, input.Key(Key(0x41, false)));
        Assert.True(input.DrainFinished(DateTimeOffset.UnixEpoch.AddMilliseconds(5), out var expired));
        Assert.False(expired);
    }
    [Fact]
    public void Intercepted_system_chord_tails_do_not_launch_foreground_shortcuts()
    {
        var input = new BackgroundInputOwnership(); input.Begin([], []); input.Activate();
        input.Key(Key(0x5B, true)); input.BeginDrain(DateTimeOffset.UnixEpoch);
        Assert.Equal(BackgroundInputRoute.Consume, input.Key(Key(0x45, true)));
        Assert.Equal(BackgroundInputRoute.Consume, input.Key(Key(0x5B, false)));
        Assert.Equal(BackgroundInputRoute.Consume, input.Key(Key(0x45, false)));
        Assert.Equal(BackgroundInputRoute.Foreground, input.Key(Key(0x41, true)));
    }
    [Fact]
    public void Drain_has_an_absolute_deadline_even_if_more_chord_keys_arrive()
    {
        var input = new BackgroundInputOwnership(); input.Begin([], []); input.Activate();
        input.Key(Key(0xA0, true));
        var start = DateTimeOffset.UnixEpoch; input.BeginDrain(start);
        input.Key(Key(0x41, true)); input.BeginDrain(start.AddSeconds(1));
        Assert.False(input.DrainFinished(start.AddMilliseconds(1999), out _));
        Assert.True(input.DrainFinished(start.AddSeconds(2), out var expired));
        Assert.True(expired);
        Assert.Equal(0, input.PendingCount);
        Assert.Equal(BackgroundInputRoute.Foreground, input.Key(Key(0xA0, false)));
    }
    [Fact]
    public void Mouse_move_is_only_delivered_for_an_active_owned_drag()
    {
        var input = new BackgroundInputOwnership(); input.Begin([], []); input.Activate();
        Assert.Equal(BackgroundInputRoute.Foreground, input.Mouse(null, false, true));
        Assert.Equal(BackgroundInputRoute.Deliver, input.Mouse(BackgroundMouseButton.Left, false, false));
        Assert.Equal(BackgroundInputRoute.Deliver, input.Mouse(null, false, true));
        input.BeginDrain(DateTimeOffset.UnixEpoch);
        Assert.Equal(BackgroundInputRoute.Foreground, input.Mouse(null, false, true));
        Assert.Equal(BackgroundInputRoute.Consume, input.Mouse(BackgroundMouseButton.Left, true, false));
    }
}
