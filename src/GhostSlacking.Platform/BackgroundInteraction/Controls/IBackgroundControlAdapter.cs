using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

[Flags]
internal enum BackgroundTextCapabilities
{
    None = 0, Locate = 1, Drag = 2, Copy = 4, Paste = 8, CommitText = 16,
    Invoke = 32, Toggle = 64, ExpandCollapse = 128, SelectItem = 256, SetEmptyText = 512
}

internal interface IBackgroundControlAdapter
{
    string Name { get; }
    BackgroundTextCapabilities Capabilities { get; }
    BackgroundInputDispatchResult? DispatchMouse(BackgroundControlAccess access, BackgroundMouseInput input, Point client, ShortcutModifiers modifiers);
    BackgroundInputDispatchResult? DispatchKey(BackgroundControlAccess access, BackgroundKeyInput input, ShortcutModifiers modifiers);
    BackgroundInputDispatchResult CommitText(BackgroundControlAccess access, string text) =>
        BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.UnsupportedKey);
    void ResetDrag();
}
