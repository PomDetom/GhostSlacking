using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

internal sealed class EditBackgroundControlAdapter : IBackgroundControlAdapter
{
    private nint _anchor;
    private bool _dragging;
    public string Name => "Win32Edit";
    public BackgroundTextCapabilities Capabilities => BackgroundTextCapabilities.Locate | BackgroundTextCapabilities.Drag |
        BackgroundTextCapabilities.Copy | BackgroundTextCapabilities.Paste | BackgroundTextCapabilities.CommitText;
    public BackgroundInputDispatchResult? DispatchMouse(BackgroundControlAccess access, BackgroundMouseInput input, Point client, ShortcutModifiers modifiers)
    {
        if (input.Action is BackgroundMouseAction.Wheel or BackgroundMouseAction.HorizontalWheel) return null;
        if (input.Button is not null && input.Button != BackgroundMouseButton.Left || modifiers.HasFlag(ShortcutModifiers.Control) || modifiers.HasFlag(ShortcutModifiers.Alt))
            return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.UnsupportedMouse);
        if (input.Action != BackgroundMouseAction.Down && !_dragging) return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
        var queried = access.Query(0x00D7, 0, (nint)(((uint)(ushort)client.Y << 16) | (ushort)client.X), out var position); // EM_CHARFROMPOS, scalar Edit form only.
        if (!queried.Success) return new(queried, BackgroundInputCategory.EditCommand);
        if ((long)position == -1) return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.OutsideEditor);
        var size = access.Query(0x000E, 0, 0, out var length); // Length only, never document content.
        if (!size.Success) return new(size, BackgroundInputCategory.EditCommand);
        if (length > 65535) return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.UnsupportedMouse);
        var caret = (nint)((long)position & 0xFFFF);
        if (input.Action == BackgroundMouseAction.Down)
        {
            _anchor = caret;
            if (modifiers.HasFlag(ShortcutModifiers.Shift))
            {
                var selected = access.Query(0x00B0, 0, 0, out var selection); // EM_GETSEL packed form.
                if (!selected.Success) return new(selected, BackgroundInputCategory.EditCommand);
                _anchor = (nint)((long)selection & 0xFFFF);
            }
            _dragging = true;
        }
        var result = access.Command(0x00B1, _anchor, caret); // EM_SETSEL, no SetFocus.
        if (input.Action == BackgroundMouseAction.Up) ResetDrag();
        return result;
    }
    public BackgroundInputDispatchResult? DispatchKey(BackgroundControlAccess access, BackgroundKeyInput input, ShortcutModifiers modifiers)
    {
        if (!input.IsDown) return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
        if (modifiers.HasFlag(ShortcutModifiers.Alt)) return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.SystemShortcut);
        if (!modifiers.HasFlag(ShortcutModifiers.Control)) return null;
        if (modifiers.HasFlag(ShortcutModifiers.Shift)) return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.UnsupportedKey);
        return input.VirtualKey switch
        {
            0x41 => access.Command(0x00B1, 0, -1),
            0x43 => access.Command(0x0301), // WM_COPY
            0x58 => access.Command(0x0300), // WM_CUT
            0x56 => access.Command(0x0302), // WM_PASTE, target reads clipboard only on explicit action.
            0x5A => access.Command(0x0304), // WM_UNDO
            _ => BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.UnsupportedKey)
        };
    }
    public BackgroundInputDispatchResult CommitText(BackgroundControlAccess access, string text) => access.ReplaceSelection(text);
    public void ResetDrag() => _dragging = false;
}
