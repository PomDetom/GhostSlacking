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
        _logger.Log(LogLevel.Info, $"SnapshotSaved hwnd={target.Hwnd} pid={target.ProcessId}");
        return OperationResult<GhostWindowProfile>.Ok(profile);
    }

    public void Forget(nint hwnd) => _profiles.Remove(hwnd);

    public RestoreItemResult RestoreWindow(nint hwnd, string reason)
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
            _logger.Log(LogLevel.Warning, $"TargetClosed hwnd={hwnd}");
            return new RestoreItemResult(hwnd, true, true, "Target window no longer exists.");
        }

        if (!_windows.IsSameIdentity(profile.Original, observation))
        {
            _profiles.Remove(hwnd);
            return new RestoreItemResult(hwnd, false, true, "Target identity no longer matches the snapshot.");
        }

        var result = _visibility.Restore(hwnd, profile.Original);
        if (result.Success)
        {
            _profiles.Remove(hwnd);
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
