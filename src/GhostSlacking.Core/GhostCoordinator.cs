using System.Drawing;

namespace GhostSlacking.Core;

public sealed class GhostCoordinator
{
    private readonly IWindowApi _windows;
    private readonly RecoveryManager _recovery;
    private readonly VisibilityEngine _visibility;
    private readonly ILogger _logger;
    private GhostWindowProfile? _current;
    private GhostState _state = GhostState.Idle;
    private Point? _lastCursor;
    private Rectangle? _lastBounds;
    private CircleRegion? _lastReveal;

    public GhostCoordinator(IWindowApi windows, RecoveryManager recovery, VisibilityEngine visibility, ILogger? logger = null)
    {
        _windows = windows;
        _recovery = recovery;
        _visibility = visibility;
        _logger = logger ?? NullLogger.Instance;
    }

    public GhostState State => _state;
    public GhostWindowProfile? CurrentProfile => _current;
    public event EventHandler<StateChangedEventArgs>? StateChanged;
    public event EventHandler<string>? UserError;
    public event EventHandler? TargetClosed;

    public void BeginPicking() => SetState(GhostState.Picking);

    public void CancelPicking()
    {
        if (_state != GhostState.Picking)
        {
            return;
        }

        SetState(_current is null ? GhostState.Idle : GhostState.Ghost);
    }

    public bool SelectWindow(TargetWindow target, RevealSettings settings)
    {
        if (_current is not null)
        {
            var previous = RestoreCurrent("switch target");
            if (!previous)
            {
                return false;
            }
        }

        SetState(GhostState.Preparing);
        var registered = _recovery.CaptureAndRegister(target, settings);
        if (!registered.Success || registered.Value is null)
        {
            Fail(registered.Error ?? "Could not save the target window state.");
            SetState(GhostState.Idle);
            return false;
        }

        var profile = registered.Value;
        var ghosted = _visibility.ApplyGhost(profile);
        if (!ghosted.Success)
        {
            _recovery.RestoreWindow(profile.Hwnd, "ghost apply failed");
            Fail(ghosted.ErrorMessage ?? "Could not make the target window ghosted.");
            SetState(GhostState.Idle);
            return false;
        }

        _current = profile;
        _lastCursor = null;
        _lastBounds = target.ScreenBounds;
        _lastReveal = null;
        _logger.Log(LogLevel.Info, $"WindowSelected hwnd={target.Hwnd} pid={target.ProcessId}");
        SetState(GhostState.Ghost);
        return true;
    }

    public void UpdatePeek(Point cursorScreen, bool peekDown)
    {
        if (_current is null || _state is GhostState.Idle or GhostState.Picking or GhostState.Preparing or GhostState.Restoring)
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
            SetState(GhostState.Idle);
            TargetClosed?.Invoke(this, EventArgs.Empty);
            return;
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
                    SetState(GhostState.Ghost);
                }
                else
                {
                    Fail(hidden.ErrorMessage ?? "Could not leave Reveal mode.");
                }
            }
            return;
        }

        var region = RevealGeometry.CreateReveal(observation.ScreenBounds, cursorScreen, _current.Reveal);
        if (!cursorChanged && !geometryChanged && _state == GhostState.Reveal && _lastReveal == region)
        {
            return;
        }

        var revealed = _visibility.ApplyReveal(_current, region);
        if (revealed.Success)
        {
            _lastReveal = region;
            SetState(GhostState.Reveal);
        }
        else
        {
            _visibility.ApplyGhost(_current);
            Fail(revealed.ErrorMessage ?? "Could not update Reveal region.");
        }
    }

    public bool RestoreCurrent(string reason)
    {
        if (_current is null)
        {
            SetState(GhostState.Idle);
            return true;
        }

        SetState(GhostState.Restoring);
        var item = _recovery.RestoreWindow(_current.Hwnd, reason);
        if (item.Success)
        {
            _current = null;
            _lastReveal = null;
            _lastBounds = null;
            SetState(GhostState.Idle);
            return true;
        }

        Fail($"Restore failed: {item.Reason}");
        SetState(GhostState.RecoveryError);
        return false;
    }

    public RestoreReport RestoreAll(string reason)
    {
        SetState(GhostState.Restoring);
        var report = _recovery.RestoreAll(reason);
        if (report.Success)
        {
            _current = null;
            _lastReveal = null;
            SetState(GhostState.Idle);
        }
        else
        {
            Fail("One or more windows could not be restored.");
            SetState(GhostState.RecoveryError);
        }

        return report;
    }

    public bool ToggleGhost(string reason)
    {
        return _current is null || _state == GhostState.Idle ? false : RestoreCurrent(reason);
    }

    private void Fail(string message)
    {
        _logger.Log(LogLevel.Error, message);
        UserError?.Invoke(this, message);
    }

    private void SetState(GhostState state)
    {
        if (_state == state)
        {
            return;
        }

        var previous = _state;
        _state = state;
        _logger.Log(LogLevel.Info, $"StateChanged {previous} -> {state}");
        StateChanged?.Invoke(this, new StateChangedEventArgs(state));
    }
}
