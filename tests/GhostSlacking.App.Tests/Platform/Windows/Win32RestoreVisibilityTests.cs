using System.Runtime.InteropServices;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App.Tests;

public sealed class Win32RestoreVisibilityTests
{
    [Fact]
    public void Restoring_a_hidden_normal_window_notifies_the_target_that_it_is_shown()
        => CheckRestore(maximized: false);

    [Fact]
    public void Restoring_a_hidden_maximized_window_notifies_the_target_that_it_is_shown()
        => CheckRestore(maximized: true);

    private static void CheckRestore(bool maximized)
    {
        var receivedShow = false;
        nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
        {
            const uint WM_WINDOWPOSCHANGED = 0x0047;
            if (message == WM_WINDOWPOSCHANGED &&
                (Marshal.PtrToStructure<WindowPos>(lParam).Flags & Win32NativeMethods.SWP_SHOWWINDOW) != 0)
                receivedShow = true;
            return Win32NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
        }

        var windowClass = new Win32NativeMethods.WNDCLASSEX
        {
            Size = (uint)Marshal.SizeOf<Win32NativeMethods.WNDCLASSEX>(),
            WindowProcedure = WindowProc,
            Instance = Win32NativeMethods.GetModuleHandle(null),
            ClassName = $"GhostSlacking.RestoreTest.{Guid.NewGuid():N}"
        };
        Assert.NotEqual(0, Win32NativeMethods.RegisterClassEx(ref windowClass));
        var hwnd = Win32NativeMethods.CreateWindowEx(0, windowClass.ClassName, "Restore test",
            0x00CF0000u /* WS_OVERLAPPEDWINDOW */, 20, 20, 400, 300, 0, 0, windowClass.Instance, 0);
        try
        {
            Assert.NotEqual(0, hwnd);
            Win32NativeMethods.ShowWindow(hwnd, maximized ? Win32NativeMethods.SW_SHOWMAXIMIZED : Win32NativeMethods.SW_SHOWNORMAL);
            var api = new Win32WindowApi();
            var backend = new Win32VisibilityBackend();
            var recovery = new RecoveryManager(api, backend);
            var target = new TargetWindow { Hwnd = hwnd, ProcessId = (uint)Environment.ProcessId, ScreenBounds = api.Observe(hwnd)!.ScreenBounds };
            var captured = recovery.CaptureAndRegister(target, new RevealSettings());
            Assert.True(captured.Success, captured.Error);
            var snapshot = captured.Value!.Original;
            Assert.True(snapshot.WasVisible);
            Assert.Equal(maximized, snapshot.Placement.IsMaximized);
            Assert.True(backend.ApplyGhost(hwnd, snapshot).Success);
            Assert.False(Win32NativeMethods.IsWindowVisible(hwnd));

            receivedShow = false;
            var restored = recovery.RestoreWindow(hwnd, "visibility regression");
            Assert.True(restored.Success, restored.Reason);
            Assert.True(WindowPlacementRestoration.Matches(snapshot, api.Observe(hwnd)!));
            Assert.True(receivedShow, "Restore must deliver WM_WINDOWPOSCHANGED with SWP_SHOWWINDOW.");
        }
        finally
        {
            if (hwnd != 0) Win32NativeMethods.DestroyWindow(hwnd);
            UnregisterClass(windowClass.ClassName, windowClass.Instance);
            GC.KeepAlive(windowClass.WindowProcedure);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public nint Hwnd, InsertAfter;
        public int X, Y, Width, Height;
        public uint Flags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string className, nint instance);
}
