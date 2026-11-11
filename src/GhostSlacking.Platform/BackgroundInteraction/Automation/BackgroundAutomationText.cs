using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using GhostSlacking.Core;

namespace GhostSlacking.Platform;

// All COM references belong to one MTA. Submission and completion never block
// the application thread; a separate timer enforces stage-specific deadlines.
internal sealed partial class BackgroundAutomationText : IDisposable
{
    private readonly BlockingCollection<Action> _work = new();
    private readonly ConcurrentDictionary<long, Request> _pending = new();
    private readonly ConcurrentQueue<BackgroundInputCompletion> _completed = new();
    private readonly Thread _thread;
    private readonly Timer _deadlines;
    private readonly ILogger _logger;
    private readonly Func<(NativeResult Result, string Text)> _readPasteText;
    private readonly object _submitGate = new();
    private Request? _lastMove;
    private long _serial;
    private int _epoch;
    private volatile bool _disposed;
    private volatile bool _hasSelection;
    private IUIAutomation? _automation;
    private IUIAutomationCacheRequest? _cache;
    private IUIAutomationTreeWalker? _walker;
    private IUIAutomationCondition? _textCondition;
    private IUIAutomationElement? _rootElement;
    private IUIAutomationElement? _owner;
    private IUIAutomationTextPattern? _pattern;
    private IUIAutomationTextRange? _anchor;
    private IUIAutomationTextRange? _word;
    private nint _root;
    private Rectangle _rootBounds;
    private uint _dpi;
    private int[]? _ownerRuntime;
    private double[]? _ownerBounds;
    private long _gesture;
    private bool _gestureAccepted;
    public bool HasTextSelection => _hasSelection;

    private sealed class GuardFailure(NativeResult result) : Exception
    {
        public NativeResult Result { get; } = result;
    }
    private sealed class Request
    {
        public readonly object Gate = new();
        public required long Id, Generation;
        public required int Epoch;
        public required string Kind;
        public required Func<NativeResult> Guard;
        public required Func<Request, BackgroundInputDispatchResult> Action;
        public long Submitted = Stopwatch.GetTimestamp(), Began, StageStarted = Stopwatch.GetTimestamp(), Mutation;
        public long Gesture;
        public nint Root;
        public Point Point;
        public BackgroundMouseMotion Motion;
        public string Stage = "Queued";
        public bool Started, Terminal;
        public readonly Dictionary<string, double> Durations = [];
    }

    public BackgroundAutomationText(ILogger? logger = null, Func<(NativeResult Result, string Text)>? readPasteText = null)
    {
        _logger = logger ?? NullLogger.Instance;
        _readPasteText = readPasteText ?? (() =>
        {
            var result = BackgroundClipboard.ReadText(out var text);
            return (result, text);
        });
        _thread = new Thread(() =>
        {
            foreach (var action in _work.GetConsumingEnumerable()) action();
            ReleaseRoot(); Release(_cache); Release(_walker); Release(_textCondition); Release(_automation);
        }) { IsBackground = true, Name = "Background accessibility" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _deadlines = new Timer(CheckDeadlines, null, 25, 25);
        _thread.Start();
    }

    public IEnumerable<BackgroundInputCompletion> DrainCompletions()
    {
        while (_completed.TryDequeue(out var completion)) yield return completion;
    }

    public BackgroundInputDispatchResult Prepare(nint root, Func<NativeResult> guard, long generation) => Submit(root, 0, "Prepare", Point.Empty, guard, generation, request =>
    {
        EnsureRoot(request);
        // One provider-side filtered lookup, not FindAll + per-node round trips.
        var element = Read(request, "Discover", () => _rootElement!.FindFirstBuildCache(7, _textCondition!, _cache!));
        try { _hasSelection = element is not null && HasTextPattern(element); }
        finally { Release(element); }
        return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
    });

    public BackgroundInputDispatchResult Mouse(nint root, BackgroundMouseInput input, Point screen, ShortcutModifiers modifiers,
        Func<NativeResult> guard, long generation)
    {
        if (input.Button is not null && input.Button != BackgroundMouseButton.Left ||
            (modifiers & (ShortcutModifiers.Control | ShortcutModifiers.Alt)) != 0)
            return BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.UnsupportedMouse);
        return Submit(root, input.GestureId, input.Action.ToString(), screen, guard, generation, request =>
        {
            EnsureRoot(request);
            if (input.Action == BackgroundMouseAction.Down)
            {
                ReleaseTarget();
                _gesture = input.GestureId; _gestureAccepted = false;
                // Retry exactly once only while all work is still read-only.
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    try { if (Locate(request)) break; return Reject(BackgroundInputRejectionReason.OutsideEditor); }
                    catch (COMException error) when (attempt == 0 && error.HResult == unchecked((int)0x80040201))
                    { ReleaseRoot(); EnsureRoot(request); }
                }
            }
            else if (_gesture != input.GestureId || !_gestureAccepted)
                return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
            if (_semanticAction != SemanticAction.None)
                return HandleSemanticClick(request, input, modifiers);
            if (_isEdit)
            {
                if (input.Action == BackgroundMouseAction.Down)
                {
                    _editDownPoint = request.Point;
                    _editSelecting = input.DoubleClick || modifiers.HasFlag(ShortcutModifiers.Shift);
                    if (!_editSelecting) return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.EditCommand);
                }
                else if (!_editSelecting)
                {
                    var threshold = Math.Max(4, (int)Math.Ceiling(4d * _dpi / 96));
                    if (input.Action != BackgroundMouseAction.Move ||
                        Math.Abs((long)request.Point.X - _editDownPoint.X) <= threshold &&
                        Math.Abs((long)request.Point.Y - _editDownPoint.Y) <= threshold)
                    {
                        if (input.Action == BackgroundMouseAction.Up) _gestureAccepted = false;
                        return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
                    }
                    _editSelecting = true;
                    if (_pattern is not null)
                    {
                        _anchor = Read(request, "DragAnchor", () => _pattern.RangeFromPoint(new(_editDownPoint.X, _editDownPoint.Y)));
                        Read(request, "DragAnchor", () => _anchor.MoveEndpointByRange(1, _anchor, 0));
                    }
                }
            }
            if (_isEdit && _pattern is null)
            {
                _gestureAccepted = input.Action != BackgroundMouseAction.Up;
                return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.EditCommand);
            }
            if (_pattern is null) return Reject(BackgroundInputRejectionReason.NoInputRecipient);
            var caret = Read(request, "Range", () => _pattern.RangeFromPoint(new UiaClientPoint(request.Point.X, request.Point.Y)));
            try
            {
                Read(request, "Range", () => caret.MoveEndpointByRange(1, caret, 0));
                if (input.Action == BackgroundMouseAction.Down)
                {
                    if (input.DoubleClick) Read(request, "Range", () => caret.ExpandToEnclosingUnit(2));
                    if (modifiers.HasFlag(ShortcutModifiers.Shift))
                    {
                        var selected = Read(request, "Selection", () => _pattern.GetSelection());
                        try { if (Read(request, "Selection", selected.get_Length) == 1) _anchor = Read(request, "Selection", () => selected.GetElement(0)); }
                        finally { Release(selected); }
                        if (_anchor is not null) Read(request, "Range", () => _anchor.MoveEndpointByRange(1, _anchor, 0));
                    }
                    _anchor ??= Read(request, "Range", caret.Clone);
                    if (input.DoubleClick) _word = Read(request, "Range", caret.Clone);
                }
                if (_anchor is null) return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
                var selection = Read(request, "Range", () => (_word ?? _anchor).Clone());
                try
                {
                    var backwards = Read(request, "Range", () => caret.CompareEndpoints(0, _anchor, 0)) < 0;
                    if (_word is not null && input.Action != BackgroundMouseAction.Down)
                    {
                        if (Read(request, "Range", () => caret.CompareEndpoints(0, _word, 0)) < 0 || Read(request, "Range", () => caret.CompareEndpoints(0, _word, 1)) > 0)
                        {
                            Read(request, "Range", () => caret.ExpandToEnclosingUnit(2));
                            Read(request, "Range", () => selection.MoveEndpointByRange(backwards ? 0 : 1, caret, backwards ? 0 : 1));
                        }
                    }
                    else if (input.Action != BackgroundMouseAction.Down || !input.DoubleClick)
                        Read(request, "Range", () => selection.MoveEndpointByRange(backwards ? 0 : 1, caret, 0));
                    BeginMutation(request, "Select");
                    selection.Select();
                    Check(request);
                    _gestureAccepted = true;
                }
                finally { Release(selection); }
                if (input.Action == BackgroundMouseAction.Up) { ReleaseDrag(); _gestureAccepted = false; }
                return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.EditCommand);
            }
            finally { Release(caret); }
        }, input.Motion);
    }

    public BackgroundInputDispatchResult Copy(nint root, Func<NativeResult> guard, long generation) => Submit(root, 0, "Copy", Point.Empty, guard, generation, request =>
    {
        EnsureRoot(request);
        if (_pattern is null) return Reject(BackgroundInputRejectionReason.NoInputRecipient);
        var selected = Read(request, "Selection", () => _pattern.GetSelection());
        try
        {
            var spans = new List<string>();
            var count = Math.Min(Read(request, "Selection", selected.get_Length), 64);
            for (var index = 0; index < count; index++)
            {
                var range = Read(request, "Selection", () => selected.GetElement(index));
                try { spans.Add(Read(request, "ReadSelection", () => range.GetText(-1))); }
                finally { Release(range); }
            }
            if (!spans.Any(span => span.Length != 0)) return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
            BeginMutation(request, "Clipboard");
            var result = BackgroundClipboard.CopyText(string.Join(Environment.NewLine, spans));
            Check(request);
            return new(result, BackgroundInputCategory.EditCommand);
        }
        finally { Release(selected); }
    });

    public bool HasPendingFor(nint root, long generation) => _pending.Values.Any(request => request.Root == root && request.Generation == generation);

    public BackgroundInputDispatchResult NativeCommand(nint root, string kind, Func<NativeResult> guard, long generation,
        Func<BackgroundInputDispatchResult> command) => Submit(root, 0, kind, Point.Empty, guard, generation, request =>
    {
        BeginMutation(request, kind);
        var result = command();
        Check(request);
        return result;
    });

    private void EnsureRoot(Request request)
    {
        Check(request);
        if (_automation is null)
        {
            Stage(request, "Initialize");
            _automation = (IUIAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("ff48dba4-60ef-4201-aa87-54103eef594e"), true)!)!;
            _cache = _automation.CreateCacheRequest();
            foreach (var property in new[] { 30001, 30003, 30010, 30019, 30020, 30022, 30040 }) _cache.AddProperty(property);
            foreach (var pattern in new[] { 10000, 10002, 10005, 10010, 10014, 10015 }) _cache.AddPattern(pattern);
            _cache.put_TreeScope(1);
            _walker = _automation.get_RawViewWalker();
            _textCondition = _automation.CreatePropertyCondition(30040, true);
        }
        if (!Win32NativeMethods.GetWindowRect(request.Root, out var rect)) throw new GuardFailure(NativeResult.Failed("BackgroundInputIdentity", 0, "Target unavailable."));
        var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var dpi = Win32BackgroundInputNative.GetDpiForWindow(request.Root);
        if (_rootElement is not null && _root == request.Root && _rootBounds == bounds && _dpi == dpi) return;
        ReleaseRoot();
        _root = request.Root; _rootBounds = bounds; _dpi = dpi;
        _rootElement = Read(request, "Root", () => _automation.ElementFromHandleBuildCache(request.Root, _cache!));
    }

    private static bool Contains(double[]? bounds, Point point) => bounds is { Length: 4 } &&
        point.X >= bounds[0] && point.Y >= bounds[1] && point.X < bounds[0] + bounds[2] && point.Y < bounds[1] + bounds[3];
    private static bool EqualBounds(double[]? first, double[]? second) => first is not null && second is not null && first.SequenceEqual(second);
    private static bool HasTextPattern(IUIAutomationElement element) => element.GetCachedPropertyValue(30040) is true;
    private static BackgroundInputDispatchResult Reject(BackgroundInputRejectionReason reason) => BackgroundInputDispatchResult.Rejected(reason);

    private BackgroundInputDispatchResult Submit(nint root, long gesture, string kind, Point point, Func<NativeResult> guard,
        long generation, Func<Request, BackgroundInputDispatchResult> action, BackgroundMouseMotion? motion = null)
    {
        lock (_submitGate)
        {
            if (_disposed || _pending.Count >= 64) return Reject(BackgroundInputRejectionReason.DiscoveryUnavailable);
            if (kind == "Move" && _lastMove is { } previous)
            {
                lock (previous.Gate)
                {
                    if (!previous.Started && !previous.Terminal && previous.Epoch == _epoch && previous.Generation == generation && previous.Root == root && previous.Gesture == gesture)
                    {
                        previous.Point = point;
                        previous.Motion = previous.Motion.Include(motion ?? BackgroundMouseMotion.At(point));
                        return BackgroundInputDispatchResult.Pending(previous.Id, BackgroundInputCategory.EditCommand);
                    }
                }
            }
            var request = new Request { Id = Interlocked.Increment(ref _serial), Generation = generation, Epoch = _epoch,
                Kind = kind, Point = point, Motion = motion ?? BackgroundMouseMotion.At(point), Root = root, Gesture = gesture, Guard = guard, Action = action };
            _pending[request.Id] = request;
            _lastMove = kind == "Move" ? request : null;
            _work.Add(() => Execute(request));
            return BackgroundInputDispatchResult.Pending(request.Id, BackgroundInputCategory.EditCommand);
        }
    }

    private void Execute(Request request)
    {
        lock (request.Gate)
        {
            if (request.Terminal) return;
            request.Started = true; request.Began = Stopwatch.GetTimestamp();
        }
        try
        {
            Check(request);
            Finish(request, request.Action(request));
        }
        catch (GuardFailure error) { Finish(request, new(error.Result, BackgroundInputCategory.EditCommand)); }
        catch (COMException error)
        {
            Finish(request, request.Mutation == 0 ? Reject(BackgroundInputRejectionReason.OutsideEditor)
                : new(NativeResult.Failed("BackgroundAutomationMutation", error.HResult, "The issued provider operation failed."), BackgroundInputCategory.EditCommand), error.HResult);
        }
        catch (Exception error) { Finish(request, request.Mutation == 0 ? Reject(BackgroundInputRejectionReason.DiscoveryUnavailable)
            : new(NativeResult.Failed("BackgroundAutomationMutation", error.HResult, "The issued provider operation failed."), BackgroundInputCategory.EditCommand), error.HResult); }
    }

    private T Read<T>(Request request, string stage, Func<T> action)
    {
        Stage(request, stage); Check(request);
        var result = action();
        try { Check(request); return result; }
        catch { Release(result); throw; }
    }
    private void Read(Request request, string stage, Action action) => Read(request, stage, () => { action(); return 0; });
    private void Stage(Request request, string stage)
    {
        lock (request.Gate)
        {
            RecordStage(request);
            request.Stage = stage; request.StageStarted = Stopwatch.GetTimestamp();
        }
    }
    private static void RecordStage(Request request) => request.Durations[request.Stage] =
        request.Durations.GetValueOrDefault(request.Stage) + Stopwatch.GetElapsedTime(request.StageStarted).TotalMilliseconds;

    private void Check(Request request)
    {
        lock (request.Gate)
        {
            if (request.Terminal || request.Epoch != Volatile.Read(ref _epoch)) throw new GuardFailure(NativeResult.Failed("BackgroundAutomationCancelled", 0, "Session/request ended."));
        }
        var valid = request.Guard();
        if (!valid.Success) throw new GuardFailure(valid);
        var expired = request.Mutation == 0
            ? Stopwatch.GetElapsedTime(request.Submitted) >= BackgroundOperationDeadlines.Discovery
            : Stopwatch.GetElapsedTime(request.Mutation) >= BackgroundOperationDeadlines.Mutation;
        if (expired)
        {
            Finish(request, BackgroundOperationDeadlines.Timeout(request.Mutation != 0));
            throw new GuardFailure(NativeResult.Failed("BackgroundAutomationCancelled", 0, "Operation deadline exceeded."));
        }
    }
    private void BeginMutation(Request request, string stage)
    {
        Check(request);
        lock (request.Gate)
        {
            if (request.Terminal) throw new GuardFailure(NativeResult.Failed("BackgroundAutomationCancelled", 0, "Request ended."));
            RecordStage(request);
            request.Mutation = Stopwatch.GetTimestamp(); request.Stage = stage; request.StageStarted = request.Mutation;
        }
        Check(request);
    }
    private void Finish(Request request, BackgroundInputDispatchResult result, int hresult = 0)
    {
        lock (request.Gate)
        {
            if (request.Terminal) return;
            request.Terminal = true;
            RecordStage(request);
            _pending.TryRemove(request.Id, out _);
            _logger.Log(result.Result.Success || result.Rejection is not null ? LogLevel.Info : LogLevel.Warning,
                $"BackgroundAutomationResult generation={request.Generation} operation={request.Id} root={request.Root} kind={request.Kind} stage={request.Stage} " +
                $"queueMs={(request.Began == 0 ? Stopwatch.GetElapsedTime(request.Submitted).TotalMilliseconds : Stopwatch.GetElapsedTime(request.Submitted, request.Began).TotalMilliseconds):F1} " +
                $"elapsedMs={Stopwatch.GetElapsedTime(request.Submitted).TotalMilliseconds:F1} stageMs={Stopwatch.GetElapsedTime(request.StageStarted).TotalMilliseconds:F1} " +
                $"mutation={request.Mutation != 0} hresult={hresult} result={result.Result.Operation} rejection={result.Rejection?.ToString() ?? "None"} " +
                $"stages={string.Join(",", request.Durations.Select(stage => $"{stage.Key}:{stage.Value:F1}"))}");
            _completed.Enqueue(new(request.Generation, request.Id, result));
        }
    }
    private void CheckDeadlines(object? state)
    {
        foreach (var request in _pending.Values)
        {
            lock (request.Gate)
            {
                var expired = request.Mutation == 0 ? Stopwatch.GetElapsedTime(request.Submitted) >= BackgroundOperationDeadlines.Discovery
                    : Stopwatch.GetElapsedTime(request.Mutation) >= BackgroundOperationDeadlines.Mutation;
                if (expired) Finish(request, BackgroundOperationDeadlines.Timeout(request.Mutation != 0));
            }
        }
    }

    public void Reset()
    {
        lock (_submitGate)
        {
            Interlocked.Increment(ref _epoch); _hasSelection = false; _lastMove = null;
            Volatile.Write(ref _capabilities, 0);
            foreach (var request in _pending.Values)
            {
                lock (request.Gate)
                {
                    if (!request.Terminal) _logger.Log(LogLevel.Info,
                        $"BackgroundAutomationCancelled generation={request.Generation} operation={request.Id} root={request.Root} stage={request.Stage} issued={request.Mutation != 0}");
                    request.Terminal = true; _pending.TryRemove(request.Id, out _);
                }
            }
            while (_completed.TryDequeue(out _)) { }
            if (!_disposed) _work.Add(ReleaseRoot);
        }
    }
    private void ReleaseDrag() { Release(_anchor); Release(_word); _anchor = _word = null; }
    private void ReleaseTarget()
    {
        ClearSemanticTarget();
        ReleaseDrag(); Release(_pattern); Release(_owner);
        _pattern = null; _owner = null; _ownerRuntime = null; _ownerBounds = null; _hasSelection = false;
    }
    private void ReleaseRoot()
    {
        ReleaseTarget(); Release(_rootElement); _rootElement = null; _root = 0; _gestureAccepted = false;
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    public void Dispose()
    {
        lock (_submitGate)
        {
            if (_disposed) return;
            Reset(); _disposed = true; _deadlines.Dispose(); _work.CompleteAdding();
        }
    }
}
