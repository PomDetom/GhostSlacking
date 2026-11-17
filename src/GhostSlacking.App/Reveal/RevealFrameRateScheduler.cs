using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App;

internal readonly record struct RevealFrameRateDecision(
    bool IsRevealActive,
    PeekFrameRateLimit Limit,
    int TargetFramesPerSecond,
    int? DetectedRefreshRate,
    string? DisplayId,
    bool UsesFallback)
{
    public TimeSpan Interval => TimeSpan.FromSeconds(1D / TargetFramesPerSecond);
}

internal readonly record struct RevealFrameRateTransition(
    RevealFrameRateDecision Decision,
    bool ContextChanged,
    bool IntervalChanged);

internal static class RevealFrameRatePolicy
{
    internal const int BaselineFramesPerSecond = 60;
    internal const int MaximumFramesPerSecond = 120;
    internal const int MinimumValidRefreshRate = 20;
    internal const int MaximumValidRefreshRate = 500;

    public static RevealFrameRateDecision Create(
        bool isRevealActive,
        PeekFrameRateLimit limit,
        DisplayRefreshRateInfo? display)
    {
        if (!isRevealActive)
        {
            return new RevealFrameRateDecision(
                false,
                limit,
                BaselineFramesPerSecond,
                null,
                null,
                false);
        }

        var isValid = display is not null &&
            display.Hertz is >= MinimumValidRefreshRate and <= MaximumValidRefreshRate;
        var detectedRefreshRate = isValid ? display!.Hertz : BaselineFramesPerSecond;
        var selectedLimit = limit switch
        {
            PeekFrameRateLimit.Fps60 => 60,
            PeekFrameRateLimit.Fps90 => 90,
            PeekFrameRateLimit.Fps120 => 120,
            _ => MaximumFramesPerSecond
        };
        var target = Math.Min(detectedRefreshRate, Math.Min(selectedLimit, MaximumFramesPerSecond));
        return new RevealFrameRateDecision(
            true,
            limit,
            target,
            isValid ? detectedRefreshRate : null,
            isValid ? display!.DisplayId : null,
            !isValid);
    }
}

internal sealed class RevealFrameRateScheduler
{
    public RevealFrameRateScheduler()
    {
        Current = RevealFrameRatePolicy.Create(false, PeekFrameRateLimit.Auto, null);
    }

    public RevealFrameRateDecision Current { get; private set; }

    public RevealFrameRateTransition Update(
        bool isRevealActive,
        PeekFrameRateLimit limit,
        DisplayRefreshRateInfo? display)
    {
        var next = RevealFrameRatePolicy.Create(isRevealActive, limit, display);
        var transition = new RevealFrameRateTransition(
            next,
            next != Current,
            next.TargetFramesPerSecond != Current.TargetFramesPerSecond);
        Current = next;
        return transition;
    }
}
