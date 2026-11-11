namespace GhostSlacking.Core;

public sealed partial class GhostCoordinator
{
    private static readonly TimeSpan GeometryStableDuration = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan EnvironmentTransitionTimeout = TimeSpan.FromSeconds(2);
    private readonly TimeProvider _timeProvider;
    private WindowDisplayContext? _displayContext;
    private WindowEnvironment? _pendingEnvironment;
    private long _environmentStarted;
    private long _geometryStableSince;
    private bool _transitionWasVisible;
    private bool _allowsLateGeometry;
    private int _placementCorrections;

    public bool IsEnvironmentTransitioning => _pendingEnvironment is not null;
    public event EventHandler? EnvironmentChanging;

    // Also called before the App arms experimental input, so old coordinates
    // cannot start a new input session while the target is being rescaled.
    public bool RefreshWindowEnvironment()
    {
        if (_current is null || _windows is not IWindowEnvironmentApi api ||
            _state is not (GhostState.Ghost or GhostState.Reveal or GhostState.Visible)) return true;
        var observation = _windows.Observe(_current.Hwnd);
        if (observation is null || !_windows.IsSameIdentity(_current.Original, observation)) return false;
        var read = api.ReadEnvironment(_current.Original);
        if (!read.Success || read.Value is null)
        {
            SuspendWindowControl(read.Error ?? "Could not read display geometry.");
            return false;
        }
        var environment = read.Value;
        var now = _timeProvider.GetTimestamp();
        // Some targets finish WM_DPICHANGED after the first quiet interval.
        // Accept that late resize only inside the original two-second budget.
        var lateGeometry = _allowsLateGeometry && environment.Display == _displayContext &&
            _timeProvider.GetElapsedTime(_environmentStarted, now) < EnvironmentTransitionTimeout &&
            environment.Bounds != _current.Original.ScreenBounds;
        if (_pendingEnvironment is null && environment.Display == _displayContext && !lateGeometry) return true;
        if (_pendingEnvironment is null)
        {
            _transitionWasVisible = _state == GhostState.Visible;
            if (!lateGeometry) _environmentStarted = now;
            _geometryStableSince = now;
            _pendingEnvironment = environment;
            _lastReveal = null;
            _lastCursor = null;
            HideRevealVisual();
            _revealVisualHost?.InvalidateGeometry();
            _logger.Log(LogLevel.Info, $"DisplayTransition hwnd={_current.Hwnd} old={_displayContext} new={environment.Display} observed={environment.Bounds} expected={_current.Original.ScreenBounds}");
            EnvironmentChanging?.Invoke(this, EventArgs.Empty);
            if (!_transitionWasVisible) SetState(GhostState.Ghost);
        }
        if (!_transitionWasVisible && observation.IsVisible)
        {
            var hidden = _visibility.HideOnly(_current);
            if (!hidden.Success)
            {
                SuspendWindowControl(hidden.ErrorMessage ?? "Could not hide the rescaling target.");
                return false;
            }
        }
        // showCmd becomes SW_HIDE during Ghost. It is never a new restore state.
        var candidate = environment with
        {
            Placement = environment.Placement with
            {
                ShowCommand = _current.Original.Placement.ShowCommand,
                Flags = _current.Original.Placement.Flags
            }
        };
        var previous = _pendingEnvironment with
        {
            Placement = _pendingEnvironment.Placement with
            {
                ShowCommand = candidate.Placement.ShowCommand, Flags = candidate.Placement.Flags
            }
        };
        if (candidate != previous)
        {
            _pendingEnvironment = candidate;
            _geometryStableSince = now;
        }
        if (_timeProvider.GetElapsedTime(_environmentStarted, now) >= EnvironmentTransitionTimeout)
        {
            SuspendWindowControl("Display geometry did not stabilize within two seconds. Restore the target to continue.");
            return false;
        }
        if (_timeProvider.GetElapsedTime(_geometryStableSince, now) < GeometryStableDuration) return false;

        var updated = _recovery.UpdateGeometry(_current.Hwnd, candidate);
        if (!updated.Success || updated.Value is null)
        {
            SuspendWindowControl(updated.Error ?? "Could not register the new recovery geometry.");
            return false;
        }
        _current = updated.Value with { Reveal = _current.Reveal };
        _displayContext = candidate.Display;
        _allowsLateGeometry = true;
        _pendingEnvironment = null;
        _lastBounds = null;
        _lastCursor = null;
        _lastReveal = null;
        _placementCorrections = 0;
        _logger.Log(LogLevel.Info, $"DisplayTransitionCompleted hwnd={_current.Hwnd} bounds={candidate.Bounds} display={candidate.Display}");
        return true;
    }

    private void SuspendWindowControl(string reason)
    {
        _allowsLateGeometry = false;
        HideRevealVisual();
        _lastReveal = null;
        _pendingEnvironment = null;
        if (_current is not null && _state != GhostState.Visible)
        {
            var observation = _windows.Observe(_current.Hwnd);
            if (observation is not null && _windows.IsSameIdentity(_current.Original, observation))
            {
                var hidden = _visibility.HideOnly(_current);
                if (!hidden.Success) _logger.Log(LogLevel.Error, $"FailClosedHideFailed operation={hidden.Operation} error={hidden.ErrorCode}");
            }
        }
        SetState(GhostState.RecoveryError);
        Fail(UserErrorKind.WindowPlacementCorrectionFailed, reason);
    }

    private void ResetEnvironmentTracking()
    {
        _allowsLateGeometry = false;
        _displayContext = null;
        _pendingEnvironment = null;
        _placementCorrections = 0;
    }
}
