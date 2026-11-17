using System.Drawing;
using System.Runtime.InteropServices;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App.Tests;

[Collection("Windows UI")]
public sealed class RevealMenuRegressionTests
{
    private static readonly Win32NativeMethods.WindowProc Procedure = Win32NativeMethods.DefWindowProc;
    private static bool _registered;

    [Fact]
    public void Menu_clip_is_registered_before_mutation_and_restored_without_showing_hidden_menu()
    {
        using var fixture = new Windows();
        var visibility = new Win32VisibilityBackend();
        var recovery = new RecoveryManager(fixture.Api, visibility);
        var root = recovery.CaptureAndRegister(fixture.Target, new RevealSettings()).Value!.Original;
        var beforeMutation = false;
        recovery.ProfilesChanged += (_, _) =>
        {
            if (recovery.Find(fixture.Menu) is not null) beforeMutation |= RegionKind(fixture.Menu) == 0;
        };
        var host = new Win32RevealMenuHost(fixture.Api, recovery, visibility);
        var point = fixture.Api.Observe(fixture.Menu)!.ScreenBounds.Location + new Size(100, 100);
        var visual = Visual(fixture, point);
        var foreground = Win32BackgroundInputNative.GetForegroundWindow();
        Assert.True(host.Synchronize(root, visual, [fixture.Menu]).Success);
        Assert.True(beforeMutation);
        Assert.NotNull(recovery.Find(fixture.Menu));
        Assert.NotEqual(0, RegionKind(fixture.Menu));
        var region = Win32NativeMethods.CreateRectRgn(0, 0, 0, 0);
        try
        {
            Assert.NotEqual(0, Win32NativeMethods.GetWindowRgn(fixture.Menu, region));
            Assert.NotEqual(0, Win32NativeMethods.GetRgnBox(region, out var box));
            Assert.InRange(box.Right - box.Left, 96, 97);
            Assert.InRange(box.Bottom - box.Top, 96, 97);
        }
        finally { Win32NativeMethods.DeleteObject(region); }
        Win32NativeMethods.ShowWindow(fixture.Menu, Win32NativeMethods.SW_HIDE);
        Assert.True(host.CleanupHidden().Success);
        Assert.Equal(0, RegionKind(fixture.Menu));
        Assert.False(Win32NativeMethods.IsWindowVisible(fixture.Menu));
        Assert.Null(recovery.Find(fixture.Menu));
        Assert.NotNull(recovery.Find(fixture.Root));
        Assert.Equal(foreground, Win32BackgroundInputNative.GetForegroundWindow());
    }

    [Fact]
    public void Capture_failure_leaves_menu_region_untouched_and_unregistered()
    {
        using var fixture = new Windows();
        var visibility = new Win32VisibilityBackend();
        var api = new ControlledApi(fixture.Api) { FailCapture = fixture.Menu };
        var recovery = new RecoveryManager(api, visibility);
        var root = recovery.CaptureAndRegister(fixture.Target, new RevealSettings()).Value!.Original;
        var host = new Win32RevealMenuHost(api, recovery, visibility);
        Assert.False(host.Synchronize(root, Visual(fixture, fixture.Target.ScreenBounds.Location), [fixture.Menu]).Success);
        Assert.Equal(0, RegionKind(fixture.Menu));
        Assert.Null(recovery.Find(fixture.Menu));
    }

    [Fact]
    public void Reused_identity_is_forgotten_without_replaying_the_original_region()
    {
        using var fixture = new Windows();
        var visibility = new Win32VisibilityBackend();
        var api = new ControlledApi(fixture.Api);
        var recovery = new RecoveryManager(api, visibility);
        var root = recovery.CaptureAndRegister(fixture.Target, new RevealSettings()).Value!.Original;
        var host = new Win32RevealMenuHost(api, recovery, visibility);
        Assert.True(host.Synchronize(root, Visual(fixture, fixture.Target.ScreenBounds.Location), [fixture.Menu]).Success);
        api.WrongIdentity = fixture.Menu;
        Assert.True(host.CleanupHidden().Success);
        Assert.Null(recovery.Find(fixture.Menu));
        Assert.NotEqual(0, RegionKind(fixture.Menu));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Menu_closed_on_down_does_not_make_matching_up_a_target_identity_failure(bool destroy)
    {
        using var fixture = new Windows();
        var snapshot = fixture.Api.CaptureSnapshot(fixture.Target).Value!;
        using var prototype = new FakePrototype(fixture.Menu);
        using var backend = new Win32BackgroundInputBackend(fixture.Api, NullLogger.Instance, prototype: prototype);
        try
        {
            Assert.True(backend.SetNoActivate(snapshot, false).Success);
            var point = fixture.Api.Observe(fixture.Menu)!.ScreenBounds.Location + new Size(10, 10);
            var recipient = backend.ResolveMouseTarget(snapshot, point);
            Assert.Equal(fixture.Menu, recipient);
            Assert.True(backend.DispatchMouse(snapshot, recipient, new(BackgroundMouseAction.Down, BackgroundMouseButton.Left), point).Result.Success);
            if (destroy) Win32NativeMethods.DestroyWindow(fixture.Menu);
            else Win32NativeMethods.ShowWindow(fixture.Menu, Win32NativeMethods.SW_HIDE);
            Assert.True(backend.DispatchMouse(snapshot, recipient, new(BackgroundMouseAction.Up, BackgroundMouseButton.Left), point).Result.Success);
            Assert.Equal(new uint[] { 0x0201, 0x0202 }, prototype.Messages.Select(item => item.Message));
            Assert.All(prototype.Messages, item => Assert.Equal(recipient, item.Recipient));
        }
        finally { backend.SetNoActivate(snapshot, true); }
    }

    [Fact]
    public void Overlay_can_be_raised_above_menu_without_foreground_activation()
    {
        using var fixture = new Windows();
        using var overlay = new Win32OverlayWindow(fixture.Root);
        var foreground = Win32BackgroundInputNative.GetForegroundWindow();
        Assert.True(overlay.SetBounds(fixture.Target.ScreenBounds).Success);
        Assert.True(overlay.ShowTopmostNoActivate().Success);
        Assert.True(Win32NativeMethods.SetWindowPos(fixture.Menu, Win32NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, 0x0213));
        Assert.True(Above(fixture.Menu, overlay.Handle));
        Assert.True(overlay.ShowTopmostNoActivate().Success);
        Assert.True(Above(overlay.Handle, fixture.Menu));
        Assert.Equal(foreground, Win32BackgroundInputNative.GetForegroundWindow());
    }

    private static RevealVisualState Visual(Windows fixture, Point point)
    {
        var bounds = Rectangle.Union(fixture.Target.ScreenBounds, fixture.Api.Observe(fixture.Menu)!.ScreenBounds);
        return RevealGeometry.CreateVisualState(fixture.Root, bounds,
            RevealGeometry.CreateReveal(bounds, point, new RevealSettings { DiameterPx = 96 }), 0, RevealBlurLevel.Low);
    }
    private static int RegionKind(nint hwnd)
    {
        var region = Win32NativeMethods.CreateRectRgn(0, 0, 0, 0);
        try { return Win32NativeMethods.GetWindowRgn(hwnd, region); }
        finally { Win32NativeMethods.DeleteObject(region); }
    }
    private static bool Above(nint first, nint second)
    {
        for (var count = 0; first != 0 && count < 4096; count++, first = GetWindow(first, 2))
            if (first == second) return true;
        return false;
    }
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);

    private sealed class Windows : IDisposable
    {
        public Win32WindowApi Api { get; } = new();
        public nint Root { get; }
        public nint Menu { get; }
        public TargetWindow Target => new() { Hwnd = Root, ProcessId = (uint)Environment.ProcessId, ScreenBounds = Api.Observe(Root)!.ScreenBounds };
        public Windows()
        {
            if (!_registered)
            {
                var wc = new Win32NativeMethods.WNDCLASSEX
                {
                    Size = (uint)Marshal.SizeOf<Win32NativeMethods.WNDCLASSEX>(), WindowProcedure = Procedure,
                    Instance = Win32NativeMethods.GetModuleHandle(null), ClassName = "Chrome_WidgetWin_1"
                };
                Assert.NotEqual(0, Win32NativeMethods.RegisterClassEx(ref wc));
                _registered = true;
            }
            Root = Create("STATIC", -12000, -12000, 500, 400, 0);
            Menu = Create("Chrome_WidgetWin_1", -11500, -11900, 400, 400, Root);
        }
        private static nint Create(string name, int x, int y, int width, int height, nint owner)
        {
            var hwnd = Win32NativeMethods.CreateWindowEx(Win32NativeMethods.WS_EX_NOACTIVATE, name, "",
                Win32NativeMethods.WS_POPUP | 0x10000000, x, y, width, height, owner, 0, Win32NativeMethods.GetModuleHandle(null), 0);
            Assert.NotEqual(0, hwnd); return hwnd;
        }
        public void Dispose() { if (Win32NativeMethods.IsWindow(Menu)) Win32NativeMethods.DestroyWindow(Menu); Win32NativeMethods.DestroyWindow(Root); }
    }
    private sealed class ControlledApi(Win32WindowApi inner) : IWindowApi
    {
        public nint FailCapture { get; init; }
        public nint WrongIdentity { get; set; }
        public bool IsWindow(nint hwnd) => inner.IsWindow(hwnd);
        public WindowObservation? Observe(nint hwnd) => inner.Observe(hwnd);
        public Point GetCursorPosition() => inner.GetCursorPosition();
        public bool IsSameIdentity(WindowSnapshot snapshot, WindowObservation observed) => snapshot.Hwnd != WrongIdentity && inner.IsSameIdentity(snapshot, observed);
        public OperationResult<WindowSnapshot> CaptureSnapshot(TargetWindow target) => target.Hwnd == FailCapture
            ? OperationResult<WindowSnapshot>.Failed("Fixture capture rejected", 5) : inner.CaptureSnapshot(target);
    }
    private sealed class FakePrototype(nint menu) : IExclusiveInputPrototype
    {
        public List<(nint Recipient, uint Message)> Messages { get; } = [];
        public NativeResult Start(WindowSnapshot snapshot, nint foreground) => NativeResult.Ok("Fixture");
        public NativeResult Renew() => NativeResult.Ok("Fixture");
        public nint MenuAt(Point point) => Win32NativeMethods.IsWindow(menu) ? menu : 0;
        public bool IsMenu(nint hwnd) => hwnd == menu && Win32NativeMethods.IsWindow(hwnd) && Win32NativeMethods.IsWindowVisible(hwnd);
        public bool HasMenus => IsMenu(menu);
        public nint[] Menus() => HasMenus ? [menu] : [];
        public NativeResult PresentMenus(IReadOnlyList<nint> menus) => NativeResult.Ok("Fixture");
        public NativeResult Send(nint recipient, uint message, nint wparam, nint lparam, byte[] keyboard, Point cursor)
        { Messages.Add((recipient, message)); return NativeResult.Ok("Fixture"); }
        public void Dispose() { }
    }
}
