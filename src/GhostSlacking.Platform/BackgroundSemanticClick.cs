using System.Drawing;

namespace GhostSlacking.Platform;

// An action control is never invoked on Down or after any drag excursion.
internal sealed class BackgroundSemanticClick
{
    private Point _start;
    private long _gesture;
    private int _threshold;
    private bool _active;

    public void Begin(long gesture, Point point, int threshold)
    {
        _gesture = gesture; _start = point; _threshold = threshold; _active = true;
    }

    public void Move(long gesture, Point point)
    {
        if (gesture != _gesture || Math.Abs((long)point.X - _start.X) > _threshold ||
            Math.Abs((long)point.Y - _start.Y) > _threshold) _active = false;
    }

    public bool End(long gesture, Point point)
    {
        Move(gesture, point);
        var invoke = _active;
        _active = false;
        return invoke;
    }

    public void Cancel() => _active = false;
}
