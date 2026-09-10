using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

public sealed record DisplayRefreshRateInfo(string DisplayId, int Hertz);

public interface IDisplayRefreshRateProvider
{
    DisplayRefreshRateInfo? GetForPoint(Point point);
    void Invalidate();
}

public sealed class Win32DisplayRefreshRateProvider : IDisplayRefreshRateProvider
{
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(2);
    private readonly IDisplayRefreshRateNativeApi _nativeApi;
    private readonly IClock _clock;
    private readonly Dictionary<nint, CacheEntry> _cache = [];

    public Win32DisplayRefreshRateProvider()
        : this(new Win32DisplayRefreshRateNativeApi(), SystemClock.Instance)
    {
    }

    internal Win32DisplayRefreshRateProvider(IDisplayRefreshRateNativeApi nativeApi, IClock clock)
    {
        _nativeApi = nativeApi;
        _clock = clock;
    }

    public DisplayRefreshRateInfo? GetForPoint(Point point)
    {
        var monitor = _nativeApi.MonitorFromPoint(point);
        if (monitor == 0)
        {
            return null;
        }

        var now = _clock.UtcNow;
        if (_cache.TryGetValue(monitor, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Info;
        }

        var deviceName = _nativeApi.GetMonitorDeviceName(monitor);
        var refreshRate = string.IsNullOrWhiteSpace(deviceName)
            ? null
            : _nativeApi.GetCurrentRefreshRate(deviceName);
        var info = deviceName is not null && refreshRate.HasValue
            ? new DisplayRefreshRateInfo(deviceName, refreshRate.Value)
            : null;
        _cache[monitor] = new CacheEntry(info, now + CacheDuration);
        return info;
    }

    public void Invalidate() => _cache.Clear();

    private sealed record CacheEntry(DisplayRefreshRateInfo? Info, DateTimeOffset ExpiresAt);
}

internal interface IDisplayRefreshRateNativeApi
{
    nint MonitorFromPoint(Point point);
    string? GetMonitorDeviceName(nint monitor);
    int? GetCurrentRefreshRate(string deviceName);
}

internal sealed class Win32DisplayRefreshRateNativeApi : IDisplayRefreshRateNativeApi
{
    public nint MonitorFromPoint(Point point) => Win32NativeMethods.MonitorFromPoint(
        new Win32NativeMethods.POINT { X = point.X, Y = point.Y },
        Win32NativeMethods.MONITOR_DEFAULTTONEAREST);

    public string? GetMonitorDeviceName(nint monitor)
    {
        var monitorInfo = new Win32NativeMethods.MONITORINFOEX
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32NativeMethods.MONITORINFOEX>(),
            DeviceName = string.Empty
        };
        return Win32NativeMethods.GetMonitorInfo(monitor, ref monitorInfo)
            ? monitorInfo.DeviceName
            : null;
    }

    public int? GetCurrentRefreshRate(string deviceName)
    {
        var mode = new Win32NativeMethods.DEVMODE
        {
            DeviceName = string.Empty,
            FormName = string.Empty,
            Size = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<Win32NativeMethods.DEVMODE>()
        };
        return Win32NativeMethods.EnumDisplaySettingsEx(
            deviceName,
            Win32NativeMethods.ENUM_CURRENT_SETTINGS,
            ref mode,
            0)
                ? (int)mode.DisplayFrequency
                : null;
    }
}
