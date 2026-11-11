
namespace GhostSlacking.Core;

public sealed record WatchdogTargetIdentity(long Hwnd, uint ProcessId, string? ProcessStartIdentity);

public interface IWatchdogRecoveryTarget
{
    WatchdogTargetIdentity? Observe(long hwnd);
    NativeResult Restore(WatchdogRecoveryItem item);
}
