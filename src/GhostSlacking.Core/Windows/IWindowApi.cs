using System.Drawing;

namespace GhostSlacking.Core;

public interface IWindowApi
{
    bool IsWindow(nint hwnd);
    WindowObservation? Observe(nint hwnd);
    OperationResult<WindowSnapshot> CaptureSnapshot(TargetWindow target);
    bool IsSameIdentity(WindowSnapshot snapshot, WindowObservation observation);
    Point GetCursorPosition();
}
