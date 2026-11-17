
namespace GhostSlacking.Core;

public sealed class StateChangedEventArgs(GhostState state) : EventArgs
{
    public GhostState State { get; } = state;
}
