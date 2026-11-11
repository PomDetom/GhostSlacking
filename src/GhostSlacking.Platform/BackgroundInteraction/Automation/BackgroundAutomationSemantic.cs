using System.Drawing;
using System.Runtime.InteropServices;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

internal sealed partial class BackgroundAutomationText
{
    private enum SemanticAction { None, Invoke, Toggle, ExpandCollapse, SelectItem }
    private SemanticAction _semanticAction;
    private object? _actionPattern;
    private IUIAutomationValuePattern? _valuePattern;
    private bool _isEdit;
    private Point _editDownPoint;
    private bool _editSelecting;
    private readonly BackgroundSemanticClick _click = new();
    private int _capabilities;
    public BackgroundTextCapabilities TargetCapabilities => (BackgroundTextCapabilities)Volatile.Read(ref _capabilities);

    private bool Locate(Request request)
    {
        // Never reuse the containing document just because its large bounds include
        // the next click: it can mask buttons, a different editor, or a changed page.
        var acquired = new List<object>();
        IUIAutomationElement? candidate = null;
        IUIAutomationTextPattern? text = null;
        IUIAutomationValuePattern? value = null;
        object? actionPattern = null;
        var action = SemanticAction.None;
        var isEdit = false;
        var textFallback = false;
        var inside = false;
        try
        {
            var node = Read(request, "HitTest", () => _automation!.ElementFromPointBuildCache(new(request.Point.X, request.Point.Y), _cache!));
            for (var depth = 0; node is not null && depth < 32; depth++)
            {
                acquired.Add(node);
                var type = node.GetCachedPropertyValue(30003) is int controlType ? controlType : 0;
                if ((candidate is null || textFallback) && Contains(node.GetCachedPropertyValue(30001) as double[], request.Point))
                {
                    if (node.GetCachedPropertyValue(30019) is true || node.GetCachedPropertyValue(30022) is true ||
                        node.GetCachedPropertyValue(30010) is not true) return false;
                    // A nearest Edit owns input even if its text/selection provider
                    // is absent. Do not climb to the document as a substitute editor.
                    if (type == 50004)
                    {
                        candidate = node; isEdit = true; textFallback = false;
                        text = Pattern<IUIAutomationTextPattern>(node, 10014, acquired);
                        value = Pattern<IUIAutomationValuePattern>(node, 10002, acquired);
                    }
                    else
                    {
                        (action, actionPattern) = ActionPattern(node, acquired);
                        if (action != SemanticAction.None) { candidate = node; text = null; textFallback = false; }
                        else if (type is 50000 or 50002 or 50003 or 50005 or 50007 or 50011 or 50013 or 50019 or 50024)
                            return false; // Unsupported interactive control: never select its parent document.
                        else if (candidate is null && HasTextPattern(node))
                        {
                            text = Pattern<IUIAutomationTextPattern>(node, 10014, acquired);
                            if (text is not null) { candidate = node; textFallback = true; }
                        }
                    }
                }
                var handle = node.GetCachedPropertyValue(30020) is int hwnd ? (nint)hwnd : 0;
                if (handle == request.Root || Read(request, "Ancestry", () => _automation!.CompareElements(node, _rootElement!)))
                { inside = true; break; }
                node = Read(request, "Ancestors", () => _walker!.GetParentElementBuildCache(node, _cache!));
            }
            // A global hit on A must not fall back to the first document in B.
            if (!inside || candidate is null) return false;
            var runtime = Read(request, "Identity", candidate.GetRuntimeId);
            var bounds = candidate.GetCachedPropertyValue(30001) as double[];
            var selectable = text is not null && Read(request, "Capability", text.get_SupportedTextSelection) != 0;
            Check(request);
            _owner = candidate; _ownerRuntime = runtime; _ownerBounds = bounds;
            _pattern = selectable ? text : null;
            _isEdit = isEdit; _valuePattern = value; _semanticAction = action; _actionPattern = actionPattern;
            acquired.Remove(candidate);
            if (selectable) acquired.Remove(text!);
            if (value is not null) acquired.Remove(value);
            if (actionPattern is not null) acquired.Remove(actionPattern);
            _hasSelection = selectable;
            var capabilities = selectable ? BackgroundTextCapabilities.Locate | BackgroundTextCapabilities.Drag | BackgroundTextCapabilities.Copy : BackgroundTextCapabilities.Locate;
            capabilities |= action switch
            {
                SemanticAction.Invoke => BackgroundTextCapabilities.Invoke,
                SemanticAction.Toggle => BackgroundTextCapabilities.Toggle,
                SemanticAction.ExpandCollapse => BackgroundTextCapabilities.ExpandCollapse,
                SemanticAction.SelectItem => BackgroundTextCapabilities.SelectItem,
                _ => BackgroundTextCapabilities.None
            };
            if (isEdit && value is not null) capabilities |= BackgroundTextCapabilities.SetEmptyText;
            Volatile.Write(ref _capabilities, (int)capabilities);
            _gestureAccepted = true;
            _click.Begin(request.Gesture, request.Point, Math.Max(4, (int)Math.Ceiling(4d * _dpi / 96)));
            _logger.Log(LogLevel.Info, $"BackgroundSemanticTarget generation={request.Generation} operation={request.Id} root={request.Root} controlType={candidate.GetCachedPropertyValue(30003)} route={(isEdit ? "Editor" : action == SemanticAction.None ? "TextSelection" : action.ToString())} capabilities={capabilities}");
            return true;
        }
        finally { foreach (var item in acquired) Release(item); }

    }

    private static T? Pattern<T>(IUIAutomationElement node, int id, List<object> acquired) where T : class
    {
        var pattern = node.GetCachedPattern(id);
        if (pattern is not null) acquired.Add(pattern);
        return pattern as T;
    }

    private static (SemanticAction, object?) ActionPattern(IUIAutomationElement node, List<object> acquired)
    {
        // Prefer stateful semantics over Invoke when a control exposes both.
        if (Pattern<IUIAutomationTogglePattern>(node, 10015, acquired) is { } toggle) return (SemanticAction.Toggle, toggle);
        if (Pattern<IUIAutomationExpandCollapsePattern>(node, 10005, acquired) is { } expand) return (SemanticAction.ExpandCollapse, expand);
        if (Pattern<IUIAutomationSelectionItemPattern>(node, 10010, acquired) is { } select) return (SemanticAction.SelectItem, select);
        if (Pattern<IUIAutomationInvokePattern>(node, 10000, acquired) is { } invoke) return (SemanticAction.Invoke, invoke);
        return (SemanticAction.None, null);
    }

    private bool ValidateSemanticTarget(Request request, bool requireBounds = true)
    {
        if (_owner is null || _ownerRuntime is null) return false;
        if (!Read(request, "Identity", _owner.GetRuntimeId).SequenceEqual(_ownerRuntime) ||
            Read(request, "Enabled", () => _owner.GetCurrentPropertyValue(30010)) is not true ||
            Read(request, "Offscreen", () => _owner.GetCurrentPropertyValue(30022)) is true ||
            Read(request, "Password", () => _owner.GetCurrentPropertyValue(30019)) is true) return false;
        if (requireBounds && !EqualBounds(Read(request, "Bounds", () => _owner.GetCurrentPropertyValue(30001)) as double[], _ownerBounds)) return false;
        // RuntimeId alone does not prove the element remains in the registered B.
        var acquired = new List<object>();
        try
        {
            var node = _owner;
            for (var depth = 0; node is not null && depth < 32; depth++)
            {
                if (Read(request, "Ancestry", () => _automation!.CompareElements(node, _rootElement!))) return true;
                node = Read(request, "Ancestry", () => _walker!.GetParentElementBuildCache(node, _cache!));
                if (node is not null) acquired.Add(node);
            }
            return false;
        }
        finally { foreach (var item in acquired) Release(item); }
    }

    private BackgroundInputDispatchResult HandleSemanticClick(Request request, BackgroundMouseInput input, ShortcutModifiers modifiers)
    {
        if (modifiers != ShortcutModifiers.None) { _click.Cancel(); return Reject(BackgroundInputRejectionReason.UnsupportedMouse); }
        if (input.Action == BackgroundMouseAction.Down) return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
        if (input.Action == BackgroundMouseAction.Move)
        {
            // Coalescing must preserve excursions, including a drag out and back.
            _click.Move(input.GestureId, request.Motion.Minimum); _click.Move(input.GestureId, request.Motion.Maximum);
            return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
        }
        _gestureAccepted = false;
        if (!_click.End(input.GestureId, request.Point) || !Contains(_ownerBounds, request.Point))
            return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
        if (!ValidateSemanticTarget(request)) return Reject(BackgroundInputRejectionReason.ControlUnavailable);
        Action execute;
        if (_actionPattern is IUIAutomationExpandCollapsePattern expand)
        {
            var state = Read(request, "ExpandState", expand.get_CurrentExpandCollapseState);
            if (state == 3) return Reject(BackgroundInputRejectionReason.ControlUnavailable);
            execute = state == 0 ? expand.Expand : expand.Collapse;
        }
        else execute = _actionPattern switch
        {
            IUIAutomationInvokePattern invoke => invoke.Invoke,
            IUIAutomationTogglePattern toggle => toggle.Toggle,
            IUIAutomationSelectionItemPattern select => select.Select,
            _ => throw new InvalidOperationException("Missing semantic action.")
        };
        BeginMutation(request, _semanticAction.ToString());
        execute();
        Check(request);
        return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.EditCommand);
    }

    public BackgroundInputDispatchResult PasteEmpty(nint root, Func<NativeResult> guard, long generation) =>
        Submit(root, 0, "PasteEmpty", Point.Empty, guard, generation, request =>
        {
            EnsureRoot(request);
            if (!_isEdit || _valuePattern is null || !ValidateSemanticTarget(request) ||
                Read(request, "EditorRole", () => _owner!.GetCurrentPropertyValue(30003)) is not int role || role != 50004)
                return Reject(BackgroundInputRejectionReason.NoInputRecipient);
            var refusal = CanAssignEmpty(request);
            if (refusal is not null) return Reject(refusal.Value);
            Stage(request, "ClipboardRead");
            var (read, text) = _readPasteText();
            Check(request);
            if (!read.Success) return Reject(BackgroundInputRejectionReason.ClipboardUnavailable);
            if (text.Length == 0) return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
            // Clipboard access and provider reads can race with a draft/page update.
            if (!ValidateSemanticTarget(request)) return Reject(BackgroundInputRejectionReason.ControlUnavailable);
            refusal = CanAssignEmpty(request);
            if (refusal is not null) return Reject(refusal.Value);
            BeginMutation(request, "SetEmptyValue");
            _valuePattern.SetValue(text);
            Check(request);
            var verified = Read(request, "VerifyValue", _valuePattern.get_CurrentValue) == text;
            return verified ? BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.EditCommand) :
                new(NativeResult.Failed("BackgroundAutomationWriteUnverified", 0, "The write was issued but its result could not be verified; do not replay."), BackgroundInputCategory.EditCommand);
        });

    private BackgroundInputRejectionReason? CanAssignEmpty(Request request)
    {
        if (Read(request, "ReadOnly", _valuePattern!.get_CurrentIsReadOnly)) return BackgroundInputRejectionReason.PasteUnavailable;
        if (Read(request, "EmptyValue", _valuePattern.get_CurrentValue) != "") return BackgroundInputRejectionReason.PasteRequiresEmptyEditor;
        // A Value of "" is insufficient: embedded images/mentions may have no
        // Value text. Fail closed if the provider cannot prove an empty text tree.
        var textCondition = _automation!.CreatePropertyCondition(30003, 50020);
        var condition = _automation.CreateNotCondition(textCondition);
        try
        {
            var child = Read(request, "RichChildren", () => _owner!.FindFirstBuildCache(4, condition, _cache!));
            try { if (child is not null) return BackgroundInputRejectionReason.RichContentUnverified; }
            finally { Release(child); }
        }
        finally { Release(condition); Release(textCondition); }
        if (_pattern is null)
            return Read(request, "EditorClass", () => _owner!.GetCurrentPropertyValue(30012)) is string name &&
                (name == "Edit" || name.StartsWith("WindowsForms10.EDIT.", StringComparison.Ordinal))
                ? null : BackgroundInputRejectionReason.RichContentUnverified;
        var document = Read(request, "EmptyDocument", _pattern.get_DocumentRange);
        try
        {
            if (Read(request, "EmptyText", () => document.GetText(1)) != "") return BackgroundInputRejectionReason.PasteRequiresEmptyEditor;
            var children = Read(request, "EmbeddedChildren", document.GetChildren);
            try { return Read(request, "EmbeddedCount", children.get_Length) == 0 ? null : BackgroundInputRejectionReason.RichContentUnverified; }
            finally { Release(children); }
        }
        finally { Release(document); }
    }

    private void ClearSemanticTarget()
    {
        _click.Cancel(); Release(_actionPattern); Release(_valuePattern);
        _actionPattern = null; _valuePattern = null; _semanticAction = SemanticAction.None; _isEdit = false;
        _editSelecting = false;
        Volatile.Write(ref _capabilities, 0);
    }
}
