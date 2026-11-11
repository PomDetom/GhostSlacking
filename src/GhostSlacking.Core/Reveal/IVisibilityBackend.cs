
namespace GhostSlacking.Core;

public interface IVisibilityBackend
{
    NativeResult HideOnly(nint hwnd);
    NativeResult ApplyGhost(nint hwnd, WindowSnapshot snapshot);
    NativeResult ApplyReveal(nint hwnd, CircleRegion region, WindowSnapshot snapshot);
    NativeResult UpdateRevealRegion(nint hwnd, CircleRegion region);
    NativeResult EnsureWindowPlacement(nint hwnd, WindowSnapshot snapshot);
    NativeResult Restore(nint hwnd, WindowSnapshot snapshot);
}
