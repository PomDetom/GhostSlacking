using System.Diagnostics;

namespace LiquidGlassSample;

// Skia draw completions, not monitor presentation timestamps.
internal sealed class FrameMeter
{
    private readonly Queue<double> _intervals = new();
    private long _previous;

    public void Record()
    {
        lock (_intervals)
        {
            var now = Stopwatch.GetTimestamp();
            if (_previous != 0) _intervals.Enqueue(Stopwatch.GetElapsedTime(_previous, now).TotalMilliseconds);
            if (_intervals.Count > 180) _intervals.Dequeue();
            _previous = now;
        }
    }

    public void Reset()
    {
        lock (_intervals) { _previous = 0; _intervals.Clear(); }
    }

    public string Read()
    {
        lock (_intervals)
        {
            if (_intervals.Count == 0) return "等待绘制";
            var sorted = _intervals.Order().ToArray();
            return $"绘制 {1000 / sorted.Average():F1} FPS · P95 帧间隔 {sorted[(int)Math.Ceiling(sorted.Length * .95) - 1]:F1} ms";
        }
    }
}
