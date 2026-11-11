namespace GhostSlacking.Core;

public sealed class RecoveryManager
{
    private readonly IWindowApi _windows;
    private readonly IVisibilityBackend _visibility;
    private readonly ILogger _logger;
    private readonly Dictionary<nint, GhostWindowProfile> _profiles = new();

    public RecoveryManager(IWindowApi windows, IVisibilityBackend visibility, ILogger? logger = null)
    {
        _windows = windows;
        _visibility = visibility;
        _logger = logger ?? NullLogger.Instance;
    }

    public IReadOnlyCollection<GhostWindowProfile> Profiles => _profiles.Values;
    public event EventHandler? ProfilesChanged;

    public OperationResult<GhostWindowProfile> CaptureAndRegister(TargetWindow target, RevealSettings reveal)
    {
        if (!reveal.IsValid())
        {
            return OperationResult<GhostWindowProfile>.Failed("Reveal settings are invalid.");
        }

        var captured = _windows.CaptureSnapshot(target);
        if (!captured.Success || captured.Value is null)
        {
            _logger.Log(LogLevel.Error, $"Snapshot failed for HWND {target.Hwnd}: {captured.Error}");
            return OperationResult<GhostWindowProfile>.Failed(captured.Error ?? "Unable to capture window snapshot.", captured.ErrorCode);
        }

        var profile = new GhostWindowProfile(captured.Value, reveal);
        _profiles[target.Hwnd] = profile;
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        _logger.Log(LogLevel.Info, $"SnapshotSaved hwnd={target.Hwnd} pid={target.ProcessId}");
        return OperationResult<GhostWindowProfile>.Ok(profile);
    }

    public void Forget(nint hwnd)
    {
        if (_profiles.Remove(hwnd))
        {
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public OperationResult<GhostWindowProfile> UpdateGeometry(nint hwnd, WindowEnvironment environment)
    {
        if (!_profiles.TryGetValue(hwnd, out var profile))
            return OperationResult<GhostWindowProfile>.Failed("No recovery profile registered.");
        var observation = _windows.Observe(hwnd);
        if (observation is null || !_windows.IsSameIdentity(profile.Original, observation) ||
            observation.ScreenBounds != environment.Bounds || environment.Bounds.Width <= 0 || environment.Bounds.Height <= 0 ||
            environment.Placement.NormalPosition.Width <= 0 || environment.Placement.NormalPosition.Height <= 0 ||
            observation.IsMinimized != profile.Original.Placement.IsMinimized ||
            observation.IsMaximized != profile.Original.Placement.IsMaximized)
            return OperationResult<GhostWindowProfile>.Failed("Target identity, state or geometry changed before registration.");

        var original = profile.Original;
        var updated = profile with
        {
            Original = original with
            {
                ScreenBounds = environment.Bounds,
                Placement = original.Placement with
                {
                    NormalPosition = environment.Placement.NormalPosition,
                    MinPosition = environment.Placement.MinPosition,
                    MaxPosition = environment.Placement.MaxPosition,
                    DevicePosition = environment.Placement.DevicePosition
                }
            }
        };
        _profiles[hwnd] = updated;
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        return OperationResult<GhostWindowProfile>.Ok(updated);
    }

    public RestoreItemResult RestoreWindow(nint hwnd, string reason, bool forgetOnSuccess = true)
    {
        if (!_profiles.TryGetValue(hwnd, out var profile))
        {
            return new RestoreItemResult(hwnd, true, true, "No recovery profile registered.");
        }

        _logger.Log(LogLevel.Info, $"RestoreStarted hwnd={hwnd} reason={reason}");
        var observation = _windows.Observe(hwnd);
        if (observation is null)
        {
            _profiles.Remove(hwnd);
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
            _logger.Log(LogLevel.Warning, $"TargetClosed hwnd={hwnd}");
            return new RestoreItemResult(hwnd, true, true, "Target window no longer exists.");
        }

        if (!_windows.IsSameIdentity(profile.Original, observation))
        {
            _profiles.Remove(hwnd);
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
            return new RestoreItemResult(hwnd, false, true, "Target identity no longer matches the snapshot.");
        }

        var result = _visibility.Restore(hwnd, profile.Original);
        if (result.Success)
        {
            if (forgetOnSuccess)
            {
                _profiles.Remove(hwnd);
                ProfilesChanged?.Invoke(this, EventArgs.Empty);
            }
            _logger.Log(LogLevel.Info, $"RestoreCompleted hwnd={hwnd}");
            return new RestoreItemResult(hwnd, true, false, "Restored.");
        }

        _logger.Log(LogLevel.Error, $"Restore failed hwnd={hwnd} operation={result.Operation} error={result.ErrorCode} {result.ErrorMessage}");
        return new RestoreItemResult(hwnd, false, false, result.ErrorMessage ?? result.Operation, result.ErrorCode);
    }

    public RestoreReport RestoreAll(string reason)
    {
        var items = _profiles.Keys.ToArray().Select(hwnd => RestoreWindow(hwnd, reason)).ToArray();
        return new RestoreReport(items);
    }
}
