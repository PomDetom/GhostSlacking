using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

public sealed class Win32OverlayWindow : IDisposable
{
    private const string WindowClassName = "GhostSlacking.RevealOverlay";
    private static readonly object ClassGate = new();
    private static readonly Win32NativeMethods.WindowProc WindowProcedure = WindowProc;
    private static bool _classRegistered;
    private bool _disposed;

    public Win32OverlayWindow()
    {
        EnsureWindowClass();
        Handle = Win32NativeMethods.CreateWindowEx(
            Win32NativeMethods.WS_EX_TRANSPARENT |
            (uint)Win32NativeMethods.WS_EX_TOOLWINDOW |
            Win32NativeMethods.WS_EX_NOREDIRECTIONBITMAP |
            Win32NativeMethods.WS_EX_NOACTIVATE |
            (uint)Win32NativeMethods.WS_EX_TOPMOST,
            WindowClassName,
            WindowClassName,
            Win32NativeMethods.WS_POPUP,
            0,
            0,
            0,
            0,
            0,
            0,
            Win32NativeMethods.GetModuleHandle(null),
            0);
        if (Handle == 0)
        {
            throw new Win32Exception(Win32NativeMethods.LastError, "Could not create the Reveal overlay window.");
        }
    }

    public nint Handle { get; private set; }

    public bool IsVisible => Handle != 0 && Win32NativeMethods.IsWindowVisible(Handle);

    public NativeResult SetBounds(Rectangle bounds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Win32NativeMethods.SetWindowPos(
                Handle,
                Win32NativeMethods.HWND_TOPMOST,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                Win32NativeMethods.SWP_NOACTIVATE))
        {
            return NativeResult.Failed("SetWindowPos(RevealFeatherBounds)", Win32NativeMethods.LastError);
        }

        return NativeResult.Ok("SetWindowPos(RevealFeatherBounds)");
    }

    public NativeResult SetRingRegion(
        CircleRegion outer,
        CircleRegion inner,
        Rectangle targetBounds,
        Rectangle hostBounds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var outerHandle = CreateRegion(outer, targetBounds, hostBounds);
        var innerHandle = CreateRegion(inner, targetBounds, hostBounds);
        if (outerHandle == 0 || innerHandle == 0)
        {
            DeleteRegion(outerHandle);
            DeleteRegion(innerHandle);
            return NativeResult.Failed("CreateRgn(RevealFeather)", Win32NativeMethods.LastError);
        }

        try
        {
            if (Win32NativeMethods.CombineRgn(
                    outerHandle,
                    outerHandle,
                    innerHandle,
                    Win32NativeMethods.RGN_DIFF) == Win32NativeMethods.ERRORREGION)
            {
                return NativeResult.Failed("CombineRgn(RevealFeather)", Win32NativeMethods.LastError);
            }

            if (!Win32NativeMethods.SetWindowRgn(Handle, outerHandle, true))
            {
                return NativeResult.Failed("SetWindowRgn(RevealFeather)", Win32NativeMethods.LastError);
            }

            outerHandle = 0;
            return NativeResult.Ok("SetWindowRgn(RevealFeather)");
        }
        finally
        {
            DeleteRegion(outerHandle);
            DeleteRegion(innerHandle);
        }
    }

    public NativeResult ShowTopmostNoActivate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Win32NativeMethods.SetWindowPos(
            Handle,
            Win32NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            Win32NativeMethods.SWP_NOSIZE |
            Win32NativeMethods.SWP_NOMOVE |
            Win32NativeMethods.SWP_NOACTIVATE |
            Win32NativeMethods.SWP_SHOWWINDOW)
            ? NativeResult.Ok("SetWindowPos(RevealFeather)")
            : NativeResult.Failed("SetWindowPos(RevealFeather)", Win32NativeMethods.LastError);
    }

    public void Hide()
    {
        if (!_disposed && IsVisible)
        {
            Win32NativeMethods.ShowWindow(Handle, Win32NativeMethods.SW_HIDE);
        }
    }

    private static nint CreateRegion(CircleRegion region, Rectangle targetBounds, Rectangle hostBounds)
    {
        var bounds = RevealGeometry.GetBounds(region);
        bounds.Offset(targetBounds.Left - hostBounds.Left, targetBounds.Top - hostBounds.Top);
        return region.Shape switch
        {
            RevealShape.Circle => Win32NativeMethods.CreateEllipticRgn(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
            RevealShape.RoundedRectangle => Win32NativeMethods.CreateRoundRectRgn(
                bounds.Left,
                bounds.Top,
                bounds.Right,
                bounds.Bottom,
                region.CornerRadius * 2,
                region.CornerRadius * 2),
            _ => Win32NativeMethods.CreateRectRgn(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom)
        };
    }

    private static void DeleteRegion(nint region)
    {
        if (region != 0)
        {
            Win32NativeMethods.DeleteObject(region);
        }
    }

    private static void EnsureWindowClass()
    {
        lock (ClassGate)
        {
            if (_classRegistered)
            {
                return;
            }

            var windowClass = new Win32NativeMethods.WNDCLASSEX
            {
                Size = (uint)Marshal.SizeOf<Win32NativeMethods.WNDCLASSEX>(),
                WindowProcedure = WindowProcedure,
                Instance = Win32NativeMethods.GetModuleHandle(null),
                ClassName = WindowClassName
            };
            if (Win32NativeMethods.RegisterClassEx(ref windowClass) == 0 &&
                Win32NativeMethods.LastError != Win32NativeMethods.ERROR_CLASS_ALREADY_EXISTS)
            {
                throw new Win32Exception(Win32NativeMethods.LastError, "Could not register the Reveal overlay window class.");
            }

            _classRegistered = true;
        }
    }

    private static nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam) => message switch
    {
        Win32NativeMethods.WM_NCHITTEST => Win32NativeMethods.HTTRANSPARENT,
        Win32NativeMethods.WM_ERASEBKGND => 1,
        _ => Win32NativeMethods.DefWindowProc(hwnd, message, wParam, lParam)
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var handle = Handle;
        Handle = 0;
        if (handle != 0)
        {
            Win32NativeMethods.DestroyWindow(handle);
        }

        GC.SuppressFinalize(this);
    }
}
