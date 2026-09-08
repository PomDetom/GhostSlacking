namespace GhostSlacking.App;

internal sealed class TrayDoubleClickDetector(TimeSpan maximumInterval)
{
    private DateTimeOffset? _lastClick;

    public bool RegisterClick(DateTimeOffset timestamp)
    {
        if (_lastClick is not null &&
            timestamp >= _lastClick.Value &&
            timestamp - _lastClick.Value <= maximumInterval)
        {
            _lastClick = null;
            return true;
        }

        _lastClick = timestamp;
        return false;
    }
}
