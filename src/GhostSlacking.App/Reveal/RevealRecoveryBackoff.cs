namespace GhostSlacking.App;

internal sealed class RevealRecoveryBackoff
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1)
    ];

    private int _attempt;

    public TimeSpan NextDelay()
    {
        var delay = RetryDelays[Math.Min(_attempt, RetryDelays.Length - 1)];
        _attempt++;
        return delay;
    }

    public void Reset() => _attempt = 0;
}
