using System.Diagnostics;
using System.Globalization;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

public sealed class Win32WatchdogRecoveryTarget : IWatchdogRecoveryTarget
{
    private readonly Win32VisibilityBackend _visibility;

    public Win32WatchdogRecoveryTarget(ILogger? logger = null)
    {
        _visibility = new Win32VisibilityBackend(logger);
    }

    public WatchdogTargetIdentity? Observe(long hwndValue)
    {
        var hwnd = (nint)hwndValue;
        if (hwnd == 0 || !Win32NativeMethods.IsWindow(hwnd))
        {
            return null;
        }

        Win32NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
        {
            return null;
        }

        return new WatchdogTargetIdentity(hwndValue, processId, TryGetProcessStartIdentity(processId));
    }

    public NativeResult Restore(WatchdogRecoveryItem item) =>
        _visibility.Restore((nint)item.Hwnd, item.ToSnapshot());

    private static string? TryGetProcessStartIdentity(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }
}
