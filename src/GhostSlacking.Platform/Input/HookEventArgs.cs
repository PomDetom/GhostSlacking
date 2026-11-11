using System.Drawing;

namespace GhostSlacking.Platform;

public sealed class KeyStateChangedEventArgs(int virtualKey, bool isDown) : EventArgs
{
    public int VirtualKey { get; } = virtualKey;
    public bool IsDown { get; } = isDown;
    public bool Handled { get; set; }
}

public sealed class MouseButtonEventArgs(Point screenPoint) : EventArgs
{
    public Point ScreenPoint { get; } = screenPoint;
}
