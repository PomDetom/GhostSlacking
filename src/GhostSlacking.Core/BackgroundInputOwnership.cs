namespace GhostSlacking.Core;

public readonly record struct BackgroundInputOwnerCounts(int ForegroundKeys, int ForegroundButtons, int ConsumedKeys, int ChordKeys, int ConsumedButtons);

public enum BackgroundInputRoute { Foreground, Consume, Deliver }

// Hardware ownership is separate from messages actually delivered to B. Hooks
// may use this ledger without touching the coordinator or the target process.
public sealed class BackgroundInputOwnership
{
    public static readonly TimeSpan DrainLimit = TimeSpan.FromSeconds(2);
    private readonly object _gate = new();
    private readonly HashSet<int> _foregroundKeys = [];
    private readonly HashSet<BackgroundMouseButton> _foregroundButtons = [];
    private readonly HashSet<int> _consumedKeys = [];
    private readonly HashSet<int> _chordTail = [];
    private readonly HashSet<BackgroundMouseButton> _consumedButtons = [];
    private bool _preparing;
    private bool _active;
    private DateTimeOffset? _drainStarted;

    public BackgroundInputOwnerCounts Counts
    {
        get { lock (_gate) return new(_foregroundKeys.Count, _foregroundButtons.Count, _consumedKeys.Count, _chordTail.Count, _consumedButtons.Count); }
    }
    public int PendingCount { get { lock (_gate) return _foregroundKeys.Count + _foregroundButtons.Count + _consumedKeys.Count + _chordTail.Count + _consumedButtons.Count; } }
    public bool HasMouseDown { get { lock (_gate) return _consumedButtons.Count != 0; } }
    public bool IsForegroundKey(int key) { lock (_gate) return _foregroundKeys.Contains(key); }

    public void Begin(IEnumerable<int> keys, IEnumerable<BackgroundMouseButton> buttons)
    {
        lock (_gate)
        {
            Clear();
            _foregroundKeys.UnionWith(keys);
            _foregroundButtons.UnionWith(buttons);
            _preparing = true;
        }
    }

    public bool Activate()
    {
        lock (_gate)
        {
            if (!_preparing || _drainStarted is not null || PendingCount != 0) return false;
            _preparing = false; _active = true;
            return true;
        }
    }
    public void ConsumeMouse(BackgroundMouseButton button) { lock (_gate) _consumedButtons.Add(button); }
    public void ConsumeKey(int key) { lock (_gate) _consumedKeys.Add(key); }
    public void BeginDrain(DateTimeOffset now)
    {
        lock (_gate) { _active = _preparing = false; _drainStarted ??= now; }
    }

    public BackgroundInputRoute Key(BackgroundKeyInput input)
    {
        lock (_gate)
        {
            var key = input.VirtualKey;
            if (_foregroundKeys.Contains(key))
            {
                if (input.IsDown) return BackgroundInputRoute.Consume;
                _foregroundKeys.Remove(key);
                return BackgroundInputRoute.Foreground;
            }
            if (_active || _preparing)
            {
                if (input.IsDown) _consumedKeys.Add(key);
                else if (!_consumedKeys.Remove(key)) return BackgroundInputRoute.Foreground;
                return _active ? BackgroundInputRoute.Deliver : BackgroundInputRoute.Consume;
            }
            if (_consumedKeys.Contains(key) || _chordTail.Contains(key))
            {
                if (!input.IsDown) { _consumedKeys.Remove(key); _chordTail.Remove(key); }
                return BackgroundInputRoute.Consume;
            }
            // Protect only the continuation of an intercepted modifier chord.
            // An unrelated held character must not capture every future key.
            if (_consumedKeys.Any(IsModifier))
            {
                if (input.IsDown) _chordTail.Add(key);
                return BackgroundInputRoute.Consume;
            }
            return BackgroundInputRoute.Foreground;
        }
    }

    public BackgroundInputRoute Mouse(BackgroundMouseButton? button, bool up, bool move)
    {
        lock (_gate)
        {
            if (move) return _active && _consumedButtons.Count != 0 ? BackgroundInputRoute.Deliver : BackgroundInputRoute.Foreground;
            if (button is { } held && _foregroundButtons.Contains(held))
            {
                if (!up) return BackgroundInputRoute.Consume;
                _foregroundButtons.Remove(held);
                return BackgroundInputRoute.Foreground;
            }
            if (_active || _preparing)
            {
                if (button is { } key)
                {
                    if (!up) _consumedButtons.Add(key);
                    else if (!_consumedButtons.Remove(key)) return BackgroundInputRoute.Foreground;
                }
                return _active ? BackgroundInputRoute.Deliver : BackgroundInputRoute.Consume;
            }
            if (button is { } tail && _consumedButtons.Contains(tail))
            {
                if (up) _consumedButtons.Remove(tail);
                return BackgroundInputRoute.Consume;
            }
            return BackgroundInputRoute.Foreground;
        }
    }

    public bool DrainFinished(DateTimeOffset now, out bool expired)
    {
        lock (_gate)
        {
            expired = _drainStarted is { } start && now - start >= DrainLimit && PendingCount != 0;
            if (_drainStarted is null || (!expired && PendingCount != 0)) return false;
            Clear();
            return true;
        }
    }

    private static bool IsModifier(int key) => key is 0x10 or 0x11 or 0x12 or >= 0xA0 and <= 0xA5 or 0x5B or 0x5C;
    private void Clear()
    {
        _foregroundKeys.Clear(); _foregroundButtons.Clear(); _consumedKeys.Clear(); _chordTail.Clear(); _consumedButtons.Clear();
        _active = _preparing = false;
        _drainStarted = null;
    }
}
