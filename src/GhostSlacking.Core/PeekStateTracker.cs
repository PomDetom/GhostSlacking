namespace GhostSlacking.Core;

public sealed class PeekStateTracker
{
    private bool _wasDown;
    private bool _latched;

    public bool Update(bool isDown, PeekTrigger trigger)
    {
        if (trigger == PeekTrigger.Toggle && isDown && !_wasDown)
        {
            _latched = !_latched;
        }
        else if (trigger == PeekTrigger.Hold)
        {
            _latched = false;
        }

        _wasDown = isDown;
        return trigger == PeekTrigger.Toggle ? _latched : isDown;
    }

    public void Reset(bool isDown = false)
    {
        _wasDown = isDown;
        _latched = false;
    }
}
