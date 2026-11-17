using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

// Auxiliary menu regions share the root's recovery manifest. No menu is
// clipped until its complete snapshot has been captured and registered.
public sealed class Win32RevealMenuHost(IWindowApi windows, RecoveryManager recovery, Win32VisibilityBackend visibility)
{
    private sealed record Lease(WindowSnapshot Snapshot, CircleRegion? Region = null, Rectangle? Bounds = null);
    private readonly Dictionary<nint, Lease> _menus = [];
    public IReadOnlyList<nint> ReadyMenus { get; private set; } = [];

    public NativeResult Synchronize(WindowSnapshot root, RevealVisualState visual, IReadOnlyList<nint> menus,
        Func<nint, bool>? isLiveMenu = null)
    {
        ReadyMenus = [];
        var ready = new List<nint>();
        var rootObservation = windows.Observe(root.Hwnd);
        if (recovery.Find(root.Hwnd) is null || rootObservation is null || !windows.IsSameIdentity(root, rootObservation))
            return NativeResult.Failed("MenuRevealRootIdentity", 0);
        var clean = CleanupHidden(menus);
        if (!clean.Success) return clean;
        foreach (var hwnd in menus)
        {
            if (isLiveMenu?.Invoke(hwnd) == false) continue;
            var observed = windows.Observe(hwnd);
            if (observed is null) continue;
            if (observed.ScreenBounds.Width <= 0 || observed.ScreenBounds.Height <= 0) continue;
            if (!InScope(root, hwnd)) return NativeResult.Failed("MenuRevealScope", 0);
            if (!_menus.TryGetValue(hwnd, out var lease))
            {
                if (recovery.Find(hwnd) is not null) return NativeResult.Failed("MenuRevealAlreadyRegistered", 0);
                var captured = recovery.CaptureAndRegister(new TargetWindow
                {
                    Hwnd = hwnd, ProcessId = root.ProcessId, ScreenBounds = observed.ScreenBounds
                }, new RevealSettings());
                if (!captured.Success || captured.Value is null)
                {
                    if (windows.Observe(hwnd) is null || isLiveMenu?.Invoke(hwnd) == false) continue;
                    return NativeResult.Failed("MenuRevealCapture", captured.ErrorCode, captured.Error);
                }
                if (captured.Value.Original.ProcessStartIdentity != root.ProcessStartIdentity)
                {
                    recovery.Forget(hwnd);
                    return NativeResult.Failed("MenuRevealProcessIdentity", 0);
                }
                lease = new(captured.Value.Original);
                _menus.Add(hwnd, lease);
            }
            observed = windows.Observe(hwnd);
            if (observed is null) continue;
            if (!windows.IsSameIdentity(lease.Snapshot, observed)) return NativeResult.Failed("MenuRevealIdentity", 0);
            var region = visual.ContentRegion with
            {
                CenterX = visual.ContentRegion.CenterX + visual.WindowBounds.Left - observed.ScreenBounds.Left,
                CenterY = visual.ContentRegion.CenterY + visual.WindowBounds.Top - observed.ScreenBounds.Top
            };
            if (observed.IsVisible && lease.Region == region && lease.Bounds == observed.ScreenBounds &&
                visibility.RetainsRevealRegion(hwnd, region)) { ready.Add(hwnd); continue; }
            var applied = visibility.UpdateRevealRegion(hwnd, region);
            if (!applied.Success)
            {
                if (windows.Observe(hwnd) is null || isLiveMenu?.Invoke(hwnd) == false) continue;
                return applied;
            }
            _menus[hwnd] = lease with { Region = region, Bounds = observed.ScreenBounds };
            ready.Add(hwnd);
        }
        ReadyMenus = ready;
        return CleanupHidden(menus.Where(hwnd => isLiveMenu?.Invoke(hwnd) != false).ToArray());
    }

    public NativeResult CleanupHidden(IReadOnlyCollection<nint>? retainedMenus = null)
    {
        foreach (var (hwnd, lease) in _menus.ToArray())
        {
            if (retainedMenus?.Contains(hwnd) == true) continue;
            if (recovery.Find(hwnd) is null) { _menus.Remove(hwnd); continue; }
            var observed = windows.Observe(hwnd);
            if (observed is null || !windows.IsSameIdentity(lease.Snapshot, observed))
            {
                recovery.Forget(hwnd); _menus.Remove(hwnd); continue;
            }
            // Never show a cached menu again while restoring its region. A
            // still-visible menu remains clipped/registered until cancellation
            // finishes, or explicit Restore All recovers its complete snapshot.
            if (observed.IsVisible) continue;
            var restored = visibility.RestoreRegionOnly(hwnd, lease.Snapshot);
            if (!restored.Success) return restored;
            recovery.Forget(hwnd); _menus.Remove(hwnd);
        }
        return NativeResult.Ok("MenuRevealCleanup");
    }

    private static bool InScope(WindowSnapshot root, nint hwnd)
    {
        var rootThread = Win32NativeMethods.GetWindowThreadProcessId(root.Hwnd, out _);
        if (Win32NativeMethods.GetWindowThreadProcessId(hwnd, out var pid) != rootThread || pid != root.ProcessId) return false;
        var name = new char[128];
        var length = Win32BackgroundInputNative.GetClassName(hwnd, name, name.Length);
        if (new string(name, 0, length) != "Chrome_WidgetWin_1") return false;
        var style = (long)Win32NativeMethods.GetWindowLongPtr(hwnd, Win32NativeMethods.GWL_STYLE);
        if ((style & 0x80000000L) == 0 || (style & 0x40000000L) != 0 || (style & 0x00C00000L) == 0x00C00000L) return false;
        var owner = Win32NativeMethods.GetWindowLongPtr(hwnd, -8); // GWLP_HWNDPARENT: owner of a popup
        for (var depth = 0; owner != 0 && depth < 16; depth++)
        {
            if (Win32NativeMethods.GetWindowThreadProcessId(owner, out pid) != rootThread || pid != root.ProcessId) return false;
            if (owner == root.Hwnd) return true;
            owner = Win32NativeMethods.GetWindowLongPtr(owner, -8);
        }
        return false;
    }
}
