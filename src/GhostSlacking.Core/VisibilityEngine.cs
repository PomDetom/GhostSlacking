namespace GhostSlacking.Core;

public sealed class VisibilityEngine
{
    private readonly IVisibilityBackend _backend;
    private readonly ILogger _logger;

    public VisibilityEngine(IVisibilityBackend backend, ILogger? logger = null)
    {
        _backend = backend;
        _logger = logger ?? NullLogger.Instance;
    }

    public NativeResult ApplyGhost(GhostWindowProfile profile)
    {
        var result = _backend.ApplyGhost(profile.Hwnd);
        Log(result, $"GhostApplied hwnd={profile.Hwnd}");
        return result;
    }

    public NativeResult ApplyReveal(GhostWindowProfile profile, CircleRegion region)
    {
        var result = _backend.ApplyReveal(profile.Hwnd, region);
        Log(result, $"RevealApplied hwnd={profile.Hwnd} region={region}");
        return result;
    }

    private void Log(NativeResult result, string successMessage)
    {
        if (result.Success)
        {
            _logger.Log(LogLevel.Debug, successMessage);
        }
        else
        {
            _logger.Log(LogLevel.Error, $"NativeCallFailed operation={result.Operation} error={result.ErrorCode} {result.ErrorMessage}");
        }
    }
}
