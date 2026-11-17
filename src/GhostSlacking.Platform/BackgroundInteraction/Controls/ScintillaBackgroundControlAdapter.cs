using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

internal sealed class ScintillaBackgroundControlAdapter : IBackgroundControlAdapter
{
    private const uint GetMargins = 2253;
    private const uint GetMarginWidth = 2243;
    private const uint PositionFromPoint = 2022;
    private const uint GetAnchor = 2009;
    private const uint SetEmptySelection = 2556;
    private const uint SetSelection = 2160;
    private const uint WordStart = 2266;
    private const uint WordEnd = 2267;
    private nint _anchor;
    private (nint Start, nint End)? _word;
    private bool _dragging;

    public string Name => "Scintilla";
    public BackgroundTextCapabilities Capabilities => BackgroundTextCapabilities.Locate | BackgroundTextCapabilities.Drag |
        BackgroundTextCapabilities.Copy | BackgroundTextCapabilities.Paste | BackgroundTextCapabilities.CommitText;

    public BackgroundInputDispatchResult? DispatchMouse(BackgroundControlAccess access, BackgroundMouseInput input, Point client, ShortcutModifiers modifiers)
    {
        if (input.Action is BackgroundMouseAction.Wheel or BackgroundMouseAction.HorizontalWheel) return null;
        if (input.Button is not null && input.Button != BackgroundMouseButton.Left)
            return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.UnsupportedMouse);
        if (input.Action == BackgroundMouseAction.Move && !_dragging)
            return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
        if (input.Action == BackgroundMouseAction.Up && !_dragging)
            return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
        if (input.Action == BackgroundMouseAction.Down && (modifiers & (ShortcutModifiers.Alt | ShortcutModifiers.Control)) != 0)
            return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.UnsupportedMouse);

        // Margin clicks can trigger folding, bookmarks or container commands.
        // This adapter deliberately confines selection to the text surface.
        if (input.Action == BackgroundMouseAction.Down)
        {
            var margins = access.Query(GetMargins, 0, 0, out var count);
            if (!margins.Success) return Failure(margins);
            if (count < 0 || count > 64) return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.OutsideEditor);
            long marginWidth = 0;
            for (var index = 0; index < (int)count; index++)
            {
                var queried = access.Query(GetMarginWidth, index, 0, out var width);
                if (!queried.Success) return Failure(queried);
                marginWidth += (long)width;
            }
            if (client.X < marginWidth) return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.OutsideEditor);
        }

        var located = access.Query(PositionFromPoint, client.X, client.Y, out var position);
        if (!located.Success) return Failure(located);
        if (position < 0) return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.OutsideEditor);
        BackgroundInputDispatchResult selected;
        if (input.Action == BackgroundMouseAction.Down)
        {
            _word = null;
            if (input.DoubleClick)
            {
                var startResult = access.Query(WordStart, position, 1, out var start);
                if (!startResult.Success) return Failure(startResult);
                var endResult = access.Query(WordEnd, position, 1, out var end);
                if (!endResult.Success) return Failure(endResult);
                _anchor = start;
                _word = (start, end);
                selected = access.Command(SetSelection, start, end);
            }
            else if (modifiers.HasFlag(ShortcutModifiers.Shift))
            {
                var anchored = access.Query(GetAnchor, 0, 0, out _anchor);
                if (!anchored.Success) return Failure(anchored);
                selected = access.Command(SetSelection, _anchor, position);
            }
            else
            {
                _anchor = position;
                selected = access.Command(SetEmptySelection, position);
            }
            _dragging = selected.Result.Success;
        }
        else if (_word is { } word)
        {
            var backwards = position < word.Start;
            if (position >= word.Start && position <= word.End)
                selected = access.Command(SetSelection, word.Start, word.End);
            else
            {
                var boundary = access.Query(backwards ? WordStart : WordEnd, position, 1, out var caret);
                if (!boundary.Success) return Failure(boundary);
                selected = access.Command(SetSelection, backwards ? word.End : word.Start, caret);
            }
        }
        else selected = access.Command(SetSelection, _anchor, position);
        if (input.Action == BackgroundMouseAction.Up) ResetDrag();
        return selected;
    }

    public BackgroundInputDispatchResult? DispatchKey(BackgroundControlAccess access, BackgroundKeyInput input, ShortcutModifiers modifiers)
    {
        if (!input.IsDown) return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
        var control = modifiers.HasFlag(ShortcutModifiers.Control);
        var shift = modifiers.HasFlag(ShortcutModifiers.Shift);
        if (modifiers.HasFlag(ShortcutModifiers.Alt))
            return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.SystemShortcut);
        var key = input.VirtualKey;
        uint command = control ? key switch
        {
            0x41 when !shift => 2013, // SCI_SELECTALL
            0x43 when !shift => 2178, // SCI_COPY
            0x58 when !shift => 2177, // SCI_CUT
            0x56 when !shift => 2179, // SCI_PASTE (the target handles its own clipboard)
            0x5A => shift ? 2011U : 2176U, // SCI_REDO / SCI_UNDO
            0x59 when !shift => 2011,
            0x25 => shift ? 2309U : 2308U, // SCI_WORDLEFT[EXTEND]
            0x27 => shift ? 2311U : 2310U,
            0x24 => shift ? 2317U : 2316U, // SCI_DOCUMENTSTART[EXTEND]
            0x23 => shift ? 2319U : 2318U,
            _ => 0
        } : key switch
        {
            0x25 => shift ? 2305U : 2304U, // SCI_CHARLEFT[EXTEND]
            0x27 => shift ? 2307U : 2306U,
            0x26 => shift ? 2303U : 2302U, // SCI_LINEUP[EXTEND]
            0x28 => shift ? 2301U : 2300U,
            0x24 => shift ? 2313U : 2312U,
            0x23 => shift ? 2315U : 2314U,
            0x21 => shift ? 2321U : 2320U,
            0x22 => shift ? 2323U : 2322U,
            0x08 => 2326, // SCI_DELETEBACK
            0x2E => shift ? 2177U : 2180U, // SCI_CUT / SCI_CLEAR
            0x0D => 2329, // SCI_NEWLINE
            0x09 => shift ? 2328U : 2327U, // SCI_BACKTAB / SCI_TAB
            _ => 0
        };
        if (command != 0) return access.Command(command);
        if (!control && IsPrintableKey(key)) return null;
        return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.UnsupportedKey);
    }

    internal static bool IsPrintableKey(int key) => key is 0x20 or >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or
        >= 0x60 and <= 0x6F or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2;

    public BackgroundInputDispatchResult CommitText(BackgroundControlAccess access, string text)
    {
        foreach (var unit in text)
        {
            var result = access.Command(0x0102, unit, 1); // Unicode WM_CHAR; no clipboard substitution.
            if (!result.Result.Success) return result;
        }
        return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Character);
    }

    public void ResetDrag()
    {
        _dragging = false;
        _word = null;
    }

    private static BackgroundInputDispatchResult Failure(NativeResult result) => new(result, BackgroundInputCategory.EditCommand);
}
