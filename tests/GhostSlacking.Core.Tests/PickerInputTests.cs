using GhostSlacking.Core;

namespace GhostSlacking.Core.Tests;

public sealed class PickerInputTests
{
    [Fact]
    public void Escape_repeat_cancels_once_and_both_edges_are_intercepted()
    {
        var gate = new PickerEscapeGate();

        var down = gate.Process(true, PickerEscapeGate.EscapeVirtualKey, isDown: true);
        var repeat = gate.Process(false, PickerEscapeGate.EscapeVirtualKey, isDown: true);
        var up = gate.Process(false, PickerEscapeGate.EscapeVirtualKey, isDown: false);

        Assert.Equal(new PickerEscapeDecision(true, true, false), down);
        Assert.Equal(new PickerEscapeDecision(true, false, false), repeat);
        Assert.Equal(new PickerEscapeDecision(true, false, true), up);
        Assert.False(gate.AwaitingRelease);
    }

    [Fact]
    public void Non_escape_input_and_inactive_picker_are_not_intercepted()
    {
        var gate = new PickerEscapeGate();

        Assert.Equal(default, gate.Process(true, 0x41, isDown: true));
        Assert.Equal(default, gate.Process(false, PickerEscapeGate.EscapeVirtualKey, isDown: true));
    }
}
