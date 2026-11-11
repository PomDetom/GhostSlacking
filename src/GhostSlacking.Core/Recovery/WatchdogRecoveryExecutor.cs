
namespace GhostSlacking.Core;

public sealed class WatchdogRecoveryExecutor
{
    private readonly IWatchdogRecoveryTarget _target;
    private readonly HashSet<(Guid SessionId, Guid ManifestId)> _processed = new();

    public WatchdogRecoveryExecutor(IWatchdogRecoveryTarget target)
    {
        _target = target;
    }

    public RestoreReport Execute(RecoveryManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!_processed.Add((manifest.SessionId, manifest.ManifestId)))
        {
            return new RestoreReport(manifest.Items
                .Select(item => new RestoreItemResult((nint)item.Hwnd, true, true, "Manifest was already processed."))
                .ToArray());
        }

        if (manifest.SchemaVersion != WatchdogProtocol.ManifestSchemaVersion)
        {
            return SkipAll(manifest, "Unsupported recovery manifest version.");
        }

        var results = new List<RestoreItemResult>(manifest.Items.Count);
        foreach (var item in manifest.Items)
        {
            results.Add(RestoreOne(item));
        }

        return new RestoreReport(results);
    }

    private RestoreItemResult RestoreOne(WatchdogRecoveryItem item)
    {
        var hwnd = (nint)item.Hwnd;
        if (item.SnapshotSchemaVersion != WatchdogProtocol.SnapshotSchemaVersion)
        {
            return new RestoreItemResult(hwnd, false, true, "Unsupported recovery snapshot version.");
        }

        if (item.Hwnd == 0 || item.ProcessId == 0 || string.IsNullOrWhiteSpace(item.ProcessStartIdentity))
        {
            return new RestoreItemResult(hwnd, false, true, "Recovery identity is incomplete.");
        }

        var observed = _target.Observe(item.Hwnd);
        if (observed is null)
        {
            return new RestoreItemResult(hwnd, true, true, "Target window no longer exists.");
        }

        if (observed.Hwnd != item.Hwnd)
        {
            return new RestoreItemResult(hwnd, false, true, "HWND identity mismatch.");
        }

        if (observed.ProcessId != item.ProcessId)
        {
            return new RestoreItemResult(hwnd, false, true, "Process ID mismatch; HWND may have been reused.");
        }

        if (observed.ProcessStartIdentity is null ||
            !string.Equals(observed.ProcessStartIdentity, item.ProcessStartIdentity, StringComparison.Ordinal))
        {
            return new RestoreItemResult(hwnd, false, true, "Process start identity mismatch; PID may have been reused.");
        }

        var restored = _target.Restore(item);
        return restored.Success
            ? new RestoreItemResult(hwnd, true, false, "Restored by watchdog.")
            : new RestoreItemResult(hwnd, false, false, restored.ErrorMessage ?? restored.Operation, restored.ErrorCode);
    }

    private static RestoreReport SkipAll(RecoveryManifest manifest, string reason) =>
        new(manifest.Items
            .Select(item => new RestoreItemResult((nint)item.Hwnd, false, true, reason))
            .ToArray());
}
