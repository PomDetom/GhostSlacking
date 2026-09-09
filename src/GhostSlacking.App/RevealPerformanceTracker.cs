using System.Drawing;

namespace GhostSlacking.App;

internal sealed record RevealPerformanceSummary(
    int FrameCount,
    double EffectiveFramesPerSecond,
    TimeSpan AverageDuration,
    TimeSpan P95Duration,
    TimeSpan MaximumDuration,
    int OverBudgetCount);

internal sealed class RevealPerformanceTracker
{
    internal const int SampleWindowSize = 120;
    private static readonly TimeSpan FrameBudget = TimeSpan.FromSeconds(1D / 60D);
    private readonly List<TimeSpan> _durations = new(SampleWindowSize);
    private Point? _lastCursor;
    private TimeSpan? _windowStartedAt;

    public RevealPerformanceSummary? Record(Point cursor, TimeSpan updateDuration, TimeSpan observedAt)
    {
        if (_lastCursor == cursor)
        {
            return null;
        }

        _lastCursor = cursor;
        _windowStartedAt ??= observedAt;
        _durations.Add(updateDuration);
        if (_durations.Count < SampleWindowSize)
        {
            return null;
        }

        var ordered = _durations.Order().ToArray();
        var totalDuration = _durations.Aggregate(TimeSpan.Zero, (total, duration) => total + duration);
        var elapsed = observedAt - _windowStartedAt.Value;
        var effectiveFramesPerSecond = elapsed > TimeSpan.Zero
            ? (SampleWindowSize - 1) / elapsed.TotalSeconds
            : 0D;
        var p95Index = (int)Math.Ceiling(ordered.Length * 0.95D) - 1;
        var summary = new RevealPerformanceSummary(
            SampleWindowSize,
            effectiveFramesPerSecond,
            totalDuration / SampleWindowSize,
            ordered[p95Index],
            ordered[^1],
            _durations.Count(duration => duration > FrameBudget));

        _durations.Clear();
        _windowStartedAt = null;
        return summary;
    }

    public void Reset()
    {
        _lastCursor = null;
        _windowStartedAt = null;
        _durations.Clear();
    }
}
