using System.Drawing;
using System.Runtime.InteropServices;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App.Tests;

public sealed class Win32DisplayRefreshRateProviderTests
{
    [Fact]
    public void Native_display_structures_match_the_Win32_unicode_layout()
    {
        Assert.Equal(104, Marshal.SizeOf<Win32NativeMethods.MONITORINFOEX>());
        Assert.Equal(220, Marshal.SizeOf<Win32NativeMethods.DEVMODE>());
    }

    [Fact]
    public void Provider_maps_point_to_current_display_mode_and_caches_the_result()
    {
        var nativeApi = new FakeDisplayRefreshRateNativeApi();
        var clock = new MutableClock();
        var provider = new Win32DisplayRefreshRateProvider(nativeApi, clock);

        var first = provider.GetForPoint(new Point(10, 20));
        var second = provider.GetForPoint(new Point(30, 40));

        Assert.Equal(new DisplayRefreshRateInfo("DISPLAY-A", 144), first);
        Assert.Equal(first, second);
        Assert.Equal(2, nativeApi.MonitorCalls);
        Assert.Equal(1, nativeApi.MonitorInfoCalls);
        Assert.Equal(1, nativeApi.DisplaySettingsCalls);
    }

    [Fact]
    public void Cache_expiry_and_invalidation_requery_the_display_mode()
    {
        var nativeApi = new FakeDisplayRefreshRateNativeApi();
        var clock = new MutableClock();
        var provider = new Win32DisplayRefreshRateProvider(nativeApi, clock);
        provider.GetForPoint(Point.Empty);

        clock.UtcNow += Win32DisplayRefreshRateProvider.CacheDuration + TimeSpan.FromMilliseconds(1);
        provider.GetForPoint(Point.Empty);
        provider.Invalidate();
        provider.GetForPoint(Point.Empty);

        Assert.Equal(3, nativeApi.MonitorInfoCalls);
        Assert.Equal(3, nativeApi.DisplaySettingsCalls);
    }

    [Fact]
    public void Failed_display_queries_are_cached_and_return_no_result()
    {
        var nativeApi = new FakeDisplayRefreshRateNativeApi { DeviceName = null };
        var provider = new Win32DisplayRefreshRateProvider(nativeApi, new MutableClock());

        Assert.Null(provider.GetForPoint(Point.Empty));
        Assert.Null(provider.GetForPoint(Point.Empty));
        Assert.Equal(1, nativeApi.MonitorInfoCalls);
        Assert.Equal(0, nativeApi.DisplaySettingsCalls);
    }

    [Fact]
    public void Missing_monitor_returns_no_result()
    {
        var nativeApi = new FakeDisplayRefreshRateNativeApi { Monitor = 0 };
        var provider = new Win32DisplayRefreshRateProvider(nativeApi, new MutableClock());

        Assert.Null(provider.GetForPoint(Point.Empty));
        Assert.Equal(0, nativeApi.MonitorInfoCalls);
    }

    private sealed class FakeDisplayRefreshRateNativeApi : IDisplayRefreshRateNativeApi
    {
        public nint Monitor { get; init; } = 1;
        public string? DeviceName { get; init; } = "DISPLAY-A";
        public int? RefreshRate { get; init; } = 144;
        public int MonitorCalls { get; private set; }
        public int MonitorInfoCalls { get; private set; }
        public int DisplaySettingsCalls { get; private set; }

        public nint MonitorFromPoint(Point point)
        {
            MonitorCalls++;
            return Monitor;
        }

        public string? GetMonitorDeviceName(nint monitor)
        {
            MonitorInfoCalls++;
            return DeviceName;
        }

        public int? GetCurrentRefreshRate(string deviceName)
        {
            DisplaySettingsCalls++;
            return RefreshRate;
        }
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-09-10T00:00:00Z");
    }
}
