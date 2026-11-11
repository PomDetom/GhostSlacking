using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

internal sealed class AutomationBackgroundControlAdapter(BackgroundAutomationText automation, nint root, long generation, bool richEdit = false) : IBackgroundControlAdapter
{
    public string Name => richEdit ? "RichEditTextPattern" : "UIAutomationText";
    public BackgroundTextCapabilities Capabilities =>
        (automation.HasTextSelection ? BackgroundTextCapabilities.Locate | BackgroundTextCapabilities.Drag | BackgroundTextCapabilities.Copy : 0) |
        (richEdit ? BackgroundTextCapabilities.Paste | BackgroundTextCapabilities.CommitText : automation.TargetCapabilities);
    public BackgroundInputDispatchResult? DispatchMouse(BackgroundControlAccess access, BackgroundMouseInput input, Point client, ShortcutModifiers modifiers)
    {
        if (input.Action is BackgroundMouseAction.Wheel or BackgroundMouseAction.HorizontalWheel) return null;
        return automation.Mouse(root, input, access.ScreenPoint(client), modifiers, access.Validate, generation);
    }
    public BackgroundInputDispatchResult? DispatchKey(BackgroundControlAccess access, BackgroundKeyInput input, ShortcutModifiers modifiers)
    {
        if (!input.IsDown) return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
        if (modifiers == ShortcutModifiers.Control && input.VirtualKey == 0x43)
            return richEdit ? automation.NativeCommand(root, "NativeCopy", access.Validate, generation, () => access.Fresh().Command(0x0301))
                : automation.Copy(root, access.Validate, generation);
        if (richEdit && modifiers == ShortcutModifiers.Control && input.VirtualKey == 0x56)
            return automation.NativeCommand(root, "NativePaste", access.Validate, generation, () => access.Fresh().Command(0x0302));
        if (!richEdit && modifiers == ShortcutModifiers.Control && input.VirtualKey == 0x56)
            return automation.PasteEmpty(root, access.Validate, generation);
        if (richEdit && modifiers == ShortcutModifiers.None)
            return automation.HasPendingFor(root, generation) ? BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.PendingTextOperation) : null;
        return BackgroundInputDispatchResult.Rejected(input.VirtualKey == 0x56 && modifiers.HasFlag(ShortcutModifiers.Control)
            ? BackgroundInputRejectionReason.PasteUnavailable : BackgroundInputRejectionReason.UnsupportedKey);
    }
    public BackgroundInputDispatchResult CommitText(BackgroundControlAccess access, string text) => richEdit
        ? access.ReplaceSelection(text) : BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.UnsupportedKey);
    public void ResetDrag() => automation.Reset();
}
