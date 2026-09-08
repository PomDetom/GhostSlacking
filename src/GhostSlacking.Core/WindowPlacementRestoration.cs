namespace GhostSlacking.Core;

public static class WindowPlacementRestoration
{
    public static bool Matches(WindowSnapshot snapshot, WindowObservation observation) =>
        VisibilityMatches(snapshot, observation) && PlacementMatches(snapshot, observation);

    public static bool PlacementMatches(WindowSnapshot snapshot, WindowObservation observation)
    {
        if (snapshot.Placement.IsMinimized)
        {
            return observation.IsMinimized;
        }

        if (snapshot.Placement.IsMaximized)
        {
            return observation.IsMaximized && observation.ScreenBounds == snapshot.ScreenBounds;
        }

        return !observation.IsMinimized &&
            !observation.IsMaximized &&
            observation.ScreenBounds == snapshot.ScreenBounds;
    }

    private static bool VisibilityMatches(WindowSnapshot snapshot, WindowObservation observation) =>
        snapshot.WasVisible == observation.IsVisible;
}

public sealed class RestoreStabilityTracker
{
    public const int RequiredStableTicks = 3;
    public const int MaximumCorrections = 10;

    public int StableTicks { get; private set; }
    public int CorrectionAttempts { get; private set; }
    public bool IsStable => StableTicks >= RequiredStableTicks;
    public bool CanCorrect => CorrectionAttempts < MaximumCorrections;

    public void ObserveMatch()
    {
        StableTicks++;
    }

    public void ObserveDrift()
    {
        StableTicks = 0;
    }

    public void RecordCorrection()
    {
        if (!CanCorrect)
        {
            throw new InvalidOperationException("The restore correction limit has been reached.");
        }

        CorrectionAttempts++;
    }
}
