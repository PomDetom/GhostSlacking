namespace GhostSlacking.Core;

public enum BackgroundArmStatus { Preparing, AwaitingForeground, AwaitingRelease, Rejected }
public enum BackgroundArmRejection { None, UnsupportedSystem, Disposed, Busy, InvalidState, MissingTarget, TargetChanged, Hooks, ForegroundChanged }
public readonly record struct BackgroundArmResult(BackgroundArmStatus Status, BackgroundArmRejection Reason, NativeResult Native)
{
    public bool Success => Status != BackgroundArmStatus.Rejected;
    public string Operation => Native.Operation;
    public int ErrorCode => Native.ErrorCode;
    public static BackgroundArmResult Waiting(BackgroundArmStatus status) => new(status, BackgroundArmRejection.None, NativeResult.Ok(status.ToString()));
    public static BackgroundArmResult Reject(BackgroundArmRejection reason, int error = 0) =>
        new(BackgroundArmStatus.Rejected, reason, NativeResult.Failed($"BackgroundStart{reason}", error, reason.ToString()));
}

// Foreground absence is a precondition wait, not evidence that an input failed.
public static class BackgroundStartPolicy
{
    public static bool KeepWaiting(WindowSnapshot? requested, WindowSnapshot? current, bool peekIntent) =>
        peekIntent && requested is not null && requested == current;
    public static bool CanResume(BackgroundArmStatus? status, bool separateForeground) =>
        status != BackgroundArmStatus.AwaitingForeground || separateForeground;
    public static BackgroundArmStatus ForegroundStatus(nint foreground, nint target, bool belongsToTarget = false) =>
        foreground == 0 || foreground == target || belongsToTarget ? BackgroundArmStatus.AwaitingForeground : BackgroundArmStatus.Preparing;
}
