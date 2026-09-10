using System.Drawing;

namespace GhostSlacking.Core;

public sealed class GhostCoordinator
{
    private const int RevealValidationFailureNotificationThreshold = 3;
    private readonly IWindowApi _windows;
    private readonly RecoveryManager _recovery;
    private readonly VisibilityEngine _visibility;
    private readonly ILogger _logger;
    private readonly IRevealVisualHost? _revealVisualHost;
    private GhostWindowProfile? _current;
    private GhostState _state = GhostState.Idle;
    private GhostState _stateBeforePicking = GhostState.Idle;
    private Point? _lastCursor;
    private Rectangle? _lastBounds;
    private CircleRegion? _lastReveal;
    private bool _featherActive;
    private int _consecutiveRevealValidationFailures;
    private RestoreSession? _restoreSession;
    private PendingSelection? _pendingSelection;

    public GhostCoordinator(
        IWindowApi windows,
        RecoveryManager recovery,
        VisibilityEngine visibility,
        ILogger? logger = null,
        IRevealVisualHost? revealVisualHost = null)
    {
        _windows = windows;
        _recovery = recovery;
        _visibility = visibility;
        _logger = logger ?? NullLogger.Instance;
        _revealVisualHost = revealVisualHost;
    }

    public GhostState State => _state;
    public GhostWindowProfile? CurrentProfile => _current;
    public event EventHandler<StateChangedEventArgs>? StateChanged;
    public event EventHandler<string>? UserError;
    public event EventHandler<UserErrorEventArgs>? UserErrorOccurred;
    public event EventHandler? TargetClosed;

    public bool BeginPicking()
    {
        if (_state is GhostState.Picking or GhostState.Preparing or GhostState.Restoring)
        {
            return false;
        }

        _stateBeforePicking = _state;
        SetState(GhostState.Picking);
        return true;
    }

    public void CancelPicking()
    {
        if (_state != GhostState.Picking)
        {
            return;
        }

        SetState(_current is null ? GhostState.Idle : _stateBeforePicking);
        _stateBeforePicking = GhostState.Idle;
    }

    public bool SelectWindow(TargetWindow target, RevealSettings settings)
    {
        if (_current is not null)
        {
            _pendingSelection = new PendingSelection(target, settings);
            return StartRestore(_current, "switch target", forgetOnSuccess: true, GhostState.Idle);
        }

        return SelectNewWindow(target, settings);
    }

    private bool SelectNewWindow(TargetWindow target, RevealSettings settings)
    {
        SetState(GhostState.Preparing);
        _stateBeforePicking = GhostState.Idle;
        var registered = _recovery.CaptureAndRegister(target, settings);
        if (!registered.Success || registered.Value is null)
        {
            Fail(UserErrorKind.SelectionStateCaptureFailed, registered.Error ?? "Could not save the target window state.");
            SetState(GhostState.Idle);
            return false;
        }

        var profile = registered.Value;
        var ghosted = _visibility.ApplyGhost(profile);
        if (!ghosted.Success)
        {
            RestoreSynchronously(profile, "ghost apply failed");
            Fail(UserErrorKind.GhostActivationFailed, ghosted.ErrorMessage ?? "Could not make the target window ghosted.");
            SetState(GhostState.Idle);
            return false;
        }

        _current = profile;
        _lastCursor = null;
        _lastBounds = target.ScreenBounds;
        _lastReveal = null;
        _consecutiveRevealValidationFailures = 0;
        HideRevealVisual();
        _logger.Log(LogLevel.Info, $"WindowSelected hwnd={target.Hwnd} pid={target.ProcessId}");
        SetState(GhostState.Ghost);
        return true;
    }

    public void UpdatePeek(Point cursorScreen, bool peekDown)
    {
        if (_state == GhostState.Restoring)
        {
            ContinueRestore();
            return;
        }

        if (_current is null || _state is GhostState.Idle or GhostState.Picking or GhostState.Preparing or GhostState.Visible or GhostState.RecoveryError)
        {
            return;
        }

        var observation = _windows.Observe(_current.Hwnd);
        if (observation is null || !_windows.IsSameIdentity(_current.Original, observation))
        {
            _logger.Log(LogLevel.Warning, $"TargetClosed hwnd={_current.Hwnd}");
            _recovery.Forget(_current.Hwnd);
            _current = null;
            _lastReveal = null;
            HideRevealVisual();
            SetState(GhostState.Idle);
            TargetClosed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!WindowPlacementRestoration.PlacementMatches(_current.Original, observation))
        {
            var corrected = _visibility.EnsureWindowPlacement(_current);
            if (!corrected.Success)
            {
                _lastReveal = null;
                HideRevealVisual();
                Fail(UserErrorKind.WindowPlacementCorrectionFailed, corrected.ErrorMessage ?? "Could not preserve the target window placement.");
                StartRestore(_current, "window placement correction failed", forgetOnSuccess: true, GhostState.Idle);
                return;
            }

            observation = _windows.Observe(_current.Hwnd);
            if (observation is null || !_windows.IsSameIdentity(_current.Original, observation))
            {
                return;
            }

            if (!WindowPlacementRestoration.PlacementMatches(_current.Original, observation))
            {
                return;
            }
        }
        var geometryChanged = _lastBounds != observation.ScreenBounds;
        var cursorChanged = _lastCursor != cursorScreen;
        _lastBounds = observation.ScreenBounds;
        _lastCursor = cursorScreen;

        // Ghost mode intentionally hides the target with SW_HIDE, so IsVisible is
        // expected to be false while we are still allowed to reveal it here.
        if (!peekDown || observation.IsMinimized || !observation.ScreenBounds.Contains(cursorScreen))
        {
            if (_state == GhostState.Reveal && (cursorChanged || geometryChanged || !peekDown))
            {
                var hidden = _visibility.ApplyGhost(_current);
                if (hidden.Success)
                {
                    _lastReveal = null;
                    _consecutiveRevealValidationFailures = 0;
                    HideRevealVisual();
                    SetState(GhostState.Ghost);
                }
                else
                {
                    Fail(UserErrorKind.HideWindowFailed, hidden.ErrorMessage ?? "Could not leave Reveal mode.");
                }
            }
            return;
        }

        var region = RevealGeometry.CreateReveal(observation.ScreenBounds, cursorScreen, _current.Reveal);
        var featherRequested = _current.Reveal.SoftEdgeWidthPx > 0 && _revealVisualHost is not null;
        var featherStateStable = !featherRequested || _featherActive == _revealVisualHost!.IsAvailable;
        if (observation.IsVisible &&
            !cursorChanged &&
            !geometryChanged &&
            _state == GhostState.Reveal &&
            _lastReveal == region &&
            featherStateStable)
        {
            return;
        }

        var visual = RevealGeometry.CreateVisualState(
            _current.Hwnd,
            observation.ScreenBounds,
            region,
            _current.Reveal.SoftEdgeWidthPx,
            _current.Reveal.BlurLevel);
        var useFeather = TryPrepareRevealVisual(visual);
        var appliedRegion = useFeather ? visual.ContentRegion : visual.CoreRegion;
        var requiresFullReveal = _state != GhostState.Reveal || !observation.IsVisible;
        var revealed = requiresFullReveal
            ? _visibility.ApplyReveal(_current, appliedRegion)
            : _visibility.UpdateReveal(_current, appliedRegion);
        if (revealed.Success)
        {
            if (useFeather)
            {
                var presented = _revealVisualHost!.Present();
                if (!presented.Success)
                {
                    _logger.Log(
                        LogLevel.Warning,
                        $"Reveal feather presentation failed; using hard edge. operation={presented.Operation} error={presented.ErrorCode} {presented.ErrorMessage}");
                    HideRevealVisual();
                    revealed = _visibility.UpdateReveal(_current, visual.CoreRegion);
                    useFeather = false;
                }
            }

            if (!revealed.Success)
            {
                HideRevealVisual();
                _visibility.ApplyGhost(_current);
                Fail(UserErrorKind.RevealWindowFailed, revealed.ErrorMessage ?? "Could not fall back to the hard-edge Reveal region.");
                return;
            }

            _lastReveal = region;
            _consecutiveRevealValidationFailures = 0;
            _featherActive = useFeather;
            SetState(GhostState.Reveal);
        }
        else
        {
            if (IsTransientRevealValidationFailure(revealed))
            {
                _consecutiveRevealValidationFailures++;
                HideRevealVisual();
                _lastReveal = null;
                var hidden = _visibility.ApplyGhost(_current);
                if (hidden.Success)
                {
                    SetState(GhostState.Ghost);
                    if (_consecutiveRevealValidationFailures >= RevealValidationFailureNotificationThreshold)
                    {
                        _consecutiveRevealValidationFailures = 0;
                        Fail(UserErrorKind.RevealWindowFailed, revealed.ErrorMessage ?? "The target window repeatedly discarded the Reveal region.");
                    }
                    else
                    {
                        _logger.Log(
                            LogLevel.Warning,
                            $"Transient Reveal region validation failure; safely hidden and retrying next tick. hwnd={_current.Hwnd}");
                    }
                    return;
                }

                Fail(UserErrorKind.HideWindowFailed, hidden.ErrorMessage ?? "Could not safely hide the target after a Reveal validation failure.");
                return;
            }

            _consecutiveRevealValidationFailures = 0;
            HideRevealVisual();
            _visibility.ApplyGhost(_current);
            Fail(UserErrorKind.RevealWindowFailed, revealed.ErrorMessage ?? "Could not update Reveal region.");
        }
    }

    public bool UpdateRevealSettings(RevealSettings settings)
    {
        if (_current is null || !settings.IsValid())
        {
            return false;
        }

        _current = _current with { Reveal = settings };
        _lastReveal = null;
        return true;
    }

    public bool RestoreCurrent(string reason)
    {
        if (_current is null)
        {
            SetState(GhostState.Idle);
            return true;
        }

        return StartRestore(_current, reason, forgetOnSuccess: true, GhostState.Idle);
    }

    public RestoreReport RestoreAll(string reason)
    {
        SetState(GhostState.Restoring);
        HideRevealVisual();
        _restoreSession = null;
        _pendingSelection = null;
        var results = _recovery.Profiles
            .ToArray()
            .Select(profile => RestoreSynchronously(profile, reason))
            .ToArray();
        var report = new RestoreReport(results);
        if (report.Success)
        {
            _current = null;
            _lastReveal = null;
            SetState(GhostState.Idle);
        }
        else
        {
            Fail(UserErrorKind.RestoreFailed, "One or more windows could not be restored.");
            SetState(GhostState.RecoveryError);
        }

        return report;
    }

    public bool ToggleGhost(string reason)
    {
        return _current is null || _state == GhostState.Idle ? false : RestoreCurrent(reason);
    }

    public bool ToggleWindowVisibility(string reason)
    {
        if (_current is null)
        {
            return false;
        }

        if (_state == GhostState.Visible)
        {
            var ghosted = _visibility.ApplyGhost(_current);
            if (ghosted.Success)
            {
                _lastReveal = null;
                HideRevealVisual();
                SetState(GhostState.Ghost);
                return true;
            }

            Fail(UserErrorKind.HideWindowFailed, ghosted.ErrorMessage ?? "Could not hide the target window.");
            return false;
        }

        if (_state is not GhostState.Ghost and not GhostState.Reveal)
        {
            return false;
        }

        return StartRestore(_current, reason, forgetOnSuccess: false, GhostState.Visible);
    }

    private bool StartRestore(
        GhostWindowProfile profile,
        string reason,
        bool forgetOnSuccess,
        GhostState successState)
    {
        if (_restoreSession is not null)
        {
            return false;
        }

        SetState(GhostState.Restoring);
        HideRevealVisual();
        var restored = _recovery.RestoreWindow(profile.Hwnd, reason, forgetOnSuccess: false);
        if (!restored.Success)
        {
            _pendingSelection = null;
            Fail(UserErrorKind.RestoreFailed, $"Restore failed: {restored.Reason}");
            SetState(GhostState.RecoveryError);
            return false;
        }

        _restoreSession = new RestoreSession(profile, forgetOnSuccess, successState);
        return true;
    }

    private void ContinueRestore()
    {
        if (_restoreSession is not RestoreSession session)
        {
            return;
        }

        var observation = _windows.Observe(session.Profile.Hwnd);
        if (observation is null)
        {
            _recovery.Forget(session.Profile.Hwnd);
            _restoreSession = null;
            var pending = _pendingSelection;
            _pendingSelection = null;
            _current = null;
            _lastReveal = null;
            _lastBounds = null;
            SetState(GhostState.Idle);
            TargetClosed?.Invoke(this, EventArgs.Empty);
            if (pending is not null)
            {
                SelectNewWindow(pending.Target, pending.Settings);
            }
            return;
        }

        if (!_windows.IsSameIdentity(session.Profile.Original, observation))
        {
            Fail(UserErrorKind.RestoreFailed, "Restore verification stopped because the target window identity changed.");
            _restoreSession = null;
            _pendingSelection = null;
            SetState(GhostState.RecoveryError);
            return;
        }

        if (WindowPlacementRestoration.Matches(session.Profile.Original, observation))
        {
            session.Stability.ObserveMatch();
            if (session.Stability.IsStable)
            {
                CompleteRestore(session);
            }
            return;
        }

        session.Stability.ObserveDrift();
        if (!session.Stability.CanCorrect)
        {
            Fail(UserErrorKind.RestoreFailed, "Restore did not stabilize after 10 placement corrections.");
            _restoreSession = null;
            _pendingSelection = null;
            SetState(GhostState.RecoveryError);
            return;
        }

        session.Stability.RecordCorrection();
        var corrected = _recovery.RestoreWindow(
            session.Profile.Hwnd,
            $"restore stabilization {session.Stability.CorrectionAttempts}",
            forgetOnSuccess: false);
        if (!corrected.Success)
        {
            Fail(UserErrorKind.RestoreFailed, $"Restore stabilization failed: {corrected.Reason}");
            _restoreSession = null;
            _pendingSelection = null;
            SetState(GhostState.RecoveryError);
        }
    }

    private void CompleteRestore(RestoreSession session)
    {
        if (session.ForgetOnSuccess)
        {
            _recovery.Forget(session.Profile.Hwnd);
            _current = null;
        }

        _restoreSession = null;
        _lastReveal = null;
        _lastBounds = session.ForgetOnSuccess ? null : session.Profile.Original.ScreenBounds;

        var pending = _pendingSelection;
        _pendingSelection = null;
        if (pending is not null)
        {
            SetState(GhostState.Idle);
            SelectNewWindow(pending.Target, pending.Settings);
            return;
        }

        SetState(session.SuccessState);
    }

    private RestoreItemResult RestoreSynchronously(GhostWindowProfile profile, string reason)
    {
        var restored = _recovery.RestoreWindow(profile.Hwnd, reason, forgetOnSuccess: false);
        if (!restored.Success || restored.Skipped)
        {
            return restored;
        }

        var stability = new RestoreStabilityTracker();
        while (!stability.IsStable)
        {
            Thread.Sleep(33);
            var observation = _windows.Observe(profile.Hwnd);
            if (observation is null)
            {
                _recovery.Forget(profile.Hwnd);
                return new RestoreItemResult(profile.Hwnd, true, true, "Target window closed during restore verification.");
            }

            if (!_windows.IsSameIdentity(profile.Original, observation))
            {
                return new RestoreItemResult(profile.Hwnd, false, true, "Target identity changed during restore verification.");
            }

            if (WindowPlacementRestoration.Matches(profile.Original, observation))
            {
                stability.ObserveMatch();
                continue;
            }

            stability.ObserveDrift();
            if (!stability.CanCorrect)
            {
                return new RestoreItemResult(profile.Hwnd, false, false, "Restore did not stabilize after 10 placement corrections.");
            }

            stability.RecordCorrection();
            restored = _recovery.RestoreWindow(
                profile.Hwnd,
                $"{reason}; stabilization {stability.CorrectionAttempts}",
                forgetOnSuccess: false);
            if (!restored.Success)
            {
                return restored;
            }
        }

        _recovery.Forget(profile.Hwnd);
        return new RestoreItemResult(profile.Hwnd, true, false, "Restored and stabilized.");
    }

    private void Fail(UserErrorKind kind, string message)
    {
        _logger.Log(LogLevel.Error, message);
        UserError?.Invoke(this, message);
        UserErrorOccurred?.Invoke(this, new UserErrorEventArgs(kind, message));
    }

    private bool TryPrepareRevealVisual(RevealVisualState visual)
    {
        if (visual.FeatherWidthPx <= 0 || _revealVisualHost?.IsAvailable != true)
        {
            HideRevealVisual();
            return false;
        }

        var prepared = _revealVisualHost.Prepare(visual);
        if (prepared.Success)
        {
            return true;
        }

        _logger.Log(
            LogLevel.Warning,
            $"Reveal feather unavailable; using hard edge. operation={prepared.Operation} error={prepared.ErrorCode} {prepared.ErrorMessage}");
        HideRevealVisual();
        return false;
    }

    private static bool IsTransientRevealValidationFailure(NativeResult result) =>
        result.Operation is "GetWindowRgn(Validate)" or "GetRgnBox(Validate)";

    private void HideRevealVisual()
    {
        _revealVisualHost?.Hide();
        _featherActive = false;
    }

    private void SetState(GhostState state)
    {
        if (_state == state)
        {
            return;
        }

        var previous = _state;
        _state = state;
        _logger.Log(LogLevel.Debug, $"StateChanged {previous} -> {state}");
        StateChanged?.Invoke(this, new StateChangedEventArgs(state));
    }

    private sealed record PendingSelection(TargetWindow Target, RevealSettings Settings);

    private sealed class RestoreSession(
        GhostWindowProfile profile,
        bool forgetOnSuccess,
        GhostState successState)
    {
        public GhostWindowProfile Profile { get; } = profile;
        public bool ForgetOnSuccess { get; } = forgetOnSuccess;
        public GhostState SuccessState { get; } = successState;
        public RestoreStabilityTracker Stability { get; } = new();
    }
}
