using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace BackgroundInteractionProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--hang-test"]) { Thread.Sleep(Timeout.Infinite); return 0; }
        if (args is ["--cdp-self-test", var browser, var profile]) return ChromiumResearch.Run(browser, profile).GetAwaiter().GetResult();
        if (args is ["--inspect", var handle]) return Inspect((nint)long.Parse(handle));
        var research = args is ["--research-self-test"];
        if (args is not ["--self-test"] && !research)
        {
            Console.Error.WriteLine("Use --self-test or --inspect <decimal HWND>; run via run-probe.ps1 for a hard deadline.");
            return 2;
        }
        ApplicationConfiguration.Initialize();
        using var foreground = new ObserverForm { Text = "GhostSlacking probe observer", StartPosition = FormStartPosition.Manual, Bounds = new(610, 50, 360, 300) };
        foreground.Controls.Add(new Label { Text = "Isolated background interaction check.\nNo real messages are sent.\nDo not change focus until it closes.", AutoSize = true, Location = new(20, 30) });
        using var target = new TargetForm();
        var exit = 1;
        foreground.Shown += async (_, _) =>
        {
            target.Show();
            await Task.Delay(200);
            // Windows may reject foreground activation by a background helper.
            // Observe and protect the existing A instead of attempting activation.
            var a = Native.GetForegroundWindow();
            var b = target.Handle;
            var focusLosses = 0;
            Native.WinEvent callback = (_, _, hwnd, _, _, _, _) => { if (hwnd != a) Interlocked.Increment(ref focusLosses); };
            var hook = Native.SetWinEventHook(3, 3, 0, callback, 0, 0, 0);
            try
            {
                if (hook == 0 || a == 0 || a == b || Native.GetForegroundWindow() != a) throw new InvalidOperationException("Probe foreground/event monitor unavailable.");
                await Task.Run(() => { if (research) RunNativeResearch(target, a, b); else RunChecks(target, a, b); });
                await Task.Delay(100);
                if (focusLosses != 0) throw new InvalidOperationException("A foreground event changed during the probe.");
                Print(new { result = "Passed", foregroundEventsLost = focusLosses, realApps = "Not operated", clipboard = "Synthetic dependency; system clipboard unchanged" });
                exit = 0;
            }
            catch (Exception error) { Print(new { result = "Failed", error = error.GetType().Name, detail = error.Message }); }
            finally
            {
                if (hook != 0) Native.UnhookWinEvent(hook);
                GC.KeepAlive(callback);
                target.Close(); foreground.Close();
            }
        };
        Application.Run(foreground);
        return exit;
    }

    private static void RunChecks(TargetForm target, nint a, nint b)
    {
        var clipboardReads = 0;
        using var automation = new BackgroundAutomationText(new ProbeLogger(), () =>
        {
            Interlocked.Increment(ref clipboardReads);
            return (NativeResult.Ok("SyntheticClipboard"), "abc中文\r\nsecond");
        });
        NativeResult Guard() => Native.IsWindow(b) && Native.GetForegroundWindow() == a
            ? NativeResult.Ok("ProbeGuard") : NativeResult.Failed("ProbeForeground", 0);
        BackgroundInputDispatchResult Mouse(BackgroundMouseAction action, Point point, long gesture) =>
            Await(automation, automation.Mouse(b, new(action, action == BackgroundMouseAction.Move ? null : BackgroundMouseButton.Left, GestureId: gesture), point, ShortcutModifiers.None, Guard, 1));
        Point PointOf(Control control) => (Point)target.Invoke(() => control.PointToScreen(new Point(control.Width / 2, control.Height / 2)));
        void Click(Control control, long gesture)
        {
            var point = PointOf(control);
            Require(Mouse(BackgroundMouseAction.Down, point, gesture).Result.Success, "click down rejected");
            Require(Mouse(BackgroundMouseAction.Up, point, gesture).Result.Success, "click up rejected");
        }
        int Clicks() => (int)target.Invoke(() => target.Clicks);
        string Value() => (string)target.Invoke(() => target.Editor.Text);

        Require(Await(automation, automation.Prepare(b, Guard, 1)).Result.Success, "prepare");
        var button = PointOf(target.Counter);
        Require(Mouse(BackgroundMouseAction.Down, button, 1).Result.Success && Clicks() == 0, "button invoked on Down");
        Require(Mouse(BackgroundMouseAction.Up, button, 1).Result.Success && Clicks() == 1, "button did not invoke once");
        Mouse(BackgroundMouseAction.Up, button, 1);
        Require(Clicks() == 1, "duplicate Up invoked twice");
        Mouse(BackgroundMouseAction.Down, button, 2);
        Mouse(BackgroundMouseAction.Move, new(button.X + 20, button.Y), 2);
        Mouse(BackgroundMouseAction.Move, button, 2);
        Mouse(BackgroundMouseAction.Up, button, 2);
        Require(Clicks() == 1, "drag invoked button");
        Mouse(BackgroundMouseAction.Down, button, 21);
        var hookMotion = BackgroundMouseMotion.At(new(button.X + 20, button.Y)).Include(BackgroundMouseMotion.At(button));
        Require(Await(automation, automation.Mouse(b, new(BackgroundMouseAction.Move, GestureId: 21, Motion: hookMotion), button, ShortcutModifiers.None, Guard, 1)).Result.Success, "hook move envelope");
        Mouse(BackgroundMouseAction.Up, button, 21);
        Require(Clicks() == 1, "upstream coalesced drag invoked button");
        Mouse(BackgroundMouseAction.Down, button, 22);
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            automation.NativeCommand(b, "QueueBarrier", Guard, 1, () =>
            {
                entered.Set(); release.Wait(TimeSpan.FromSeconds(1));
                return BackgroundInputDispatchResult.Accepted(BackgroundInputCategory.Ignored);
            });
            Require(entered.Wait(TimeSpan.FromSeconds(1)), "queue barrier");
            automation.Mouse(b, new(BackgroundMouseAction.Move, GestureId: 22), new(button.X + 20, button.Y), ShortcutModifiers.None, Guard, 1);
            automation.Mouse(b, new(BackgroundMouseAction.Move, GestureId: 22), button, ShortcutModifiers.None, Guard, 1);
            var up = automation.Mouse(b, new(BackgroundMouseAction.Up, BackgroundMouseButton.Left, GestureId: 22), button, ShortcutModifiers.None, Guard, 1);
            release.Set();
            Require(Await(automation, up).Result.Success && Clicks() == 1, "coalesced drag invoked button");
        }
        Print(new { check = "Invoke release/duplicate/drag", result = "Passed" });

        Click(target.Toggle, 3);
        Require((bool)target.Invoke(() => target.Toggle.Checked), "toggle did not change");
        Print(new { check = "Toggle", result = "Passed" });

        var itemPoint = (Point)target.Invoke(() => target.List.PointToScreen(new Point(30, target.List.ItemHeight * 3 / 2)));
        Require(Mouse(BackgroundMouseAction.Down, itemPoint, 31).Result.Success && Mouse(BackgroundMouseAction.Up, itemPoint, 31).Result.Success, "selection item click");
        Require((int)target.Invoke(() => target.List.SelectedIndex) == 1, "wrong list item selected");
        var treePoint = (Point)target.Invoke(() => target.Tree.PointToScreen(new Point(target.Tree.Nodes[0].Bounds.X + 10, target.Tree.Nodes[0].Bounds.Y + target.Tree.ItemHeight / 2)));
        Require(Mouse(BackgroundMouseAction.Down, treePoint, 32).Result.Success && Mouse(BackgroundMouseAction.Up, treePoint, 32).Result.Success, "expand click");
        Require((bool)target.Invoke(() => target.Tree.Nodes[0].IsExpanded), "tree node did not expand");
        Print(new { check = "SelectionItem / ExpandCollapse", result = "Passed" });

        Click(target.Editor, 4);
        var pasted = Await(automation, automation.PasteEmpty(b, Guard, 1));
        Require(pasted.Result.Success && pasted.Rejection is null && Value() == "abc中文\r\nsecond", "empty paste did not update value: " + pasted.Result.Operation + "/" + pasted.Rejection);
        Require(clipboardReads == 1, "unexpected clipboard read count");
        var second = Await(automation, automation.PasteEmpty(b, Guard, 1));
        Require(second.Rejection == BackgroundInputRejectionReason.PasteRequiresEmptyEditor && clipboardReads == 1, "nonempty draft was not protected");
        foreach (var draft in new[] { " ", "\r\n", "existing draft" })
        {
            target.Invoke(() => target.Editor.Text = draft);
            Click(target.Editor, 5);
            Require(Await(automation, automation.PasteEmpty(b, Guard, 1)).Rejection == BackgroundInputRejectionReason.PasteRequiresEmptyEditor && Value() == draft, "draft changed");
        }
        Require(clipboardReads == 1, "rejected paste read clipboard");
        Print(new { check = "Empty paste / whitespace / multiline / existing draft", result = "Passed" });

        target.Invoke(() => { target.Editor.Clear(); target.Editor.Controls.Add(new Button { Text = "Embedded", Bounds = new(5, 5, 100, 30) }); });
        var richPoint = (Point)target.Invoke(() => target.Editor.PointToScreen(new Point(250, 60)));
        Mouse(BackgroundMouseAction.Down, richPoint, 51); Mouse(BackgroundMouseAction.Up, richPoint, 51);
        Require(Await(automation, automation.PasteEmpty(b, Guard, 1)).Rejection == BackgroundInputRejectionReason.RichContentUnverified && clipboardReads == 1, "rich child not protected");
        target.Invoke(() => { foreach (Control child in target.Editor.Controls.Cast<Control>().ToArray()) child.Dispose(); });
        Print(new { check = "Rich embedded child blocks whole-value assignment", result = "Passed" });

        Click(target.Counter, 6);
        Require(Await(automation, automation.PasteEmpty(b, Guard, 1)).Rejection == BackgroundInputRejectionReason.NoInputRecipient && clipboardReads == 1, "button retained old editor");
        target.Invoke(() => { target.Editor.Clear(); target.Editor.ReadOnly = true; });
        Click(target.Editor, 7);
        Require(Await(automation, automation.PasteEmpty(b, Guard, 1)).Rejection == BackgroundInputRejectionReason.PasteUnavailable && Value() == "", "read-only editor changed");
        automation.Reset();
        Require(Await(automation, automation.PasteEmpty(b, Guard, 1)).Rejection == BackgroundInputRejectionReason.NoInputRecipient, "reset retained target");
        Print(new { check = "Editor ownership / read-only / reset", result = "Passed" });
    }

    private static void RunNativeResearch(TargetForm target, nint a, nint b)
    {
        NativeResult Guard() => Native.IsWindow(b) && Native.GetForegroundWindow() == a
            ? NativeResult.Ok("ProbeGuard") : NativeResult.Failed("ProbeForeground", 0);
        var editor = (nint)target.Invoke(() => target.Editor.Handle);
        var access = new BackgroundControlAccess(editor, Guard);
        target.Invoke(() => target.Editor.Text = "abcd");
        Require(access.Fresh().Command(0x00B1, 1, 3).Result.Success, "native select");
        Require(access.Fresh().ReplaceSelection("中文").Result.Success, "native replacement");
        Require((bool)target.Invoke(() => target.Editor.Text == "a中文d" && target.Editor.SelectionStart == 3), "native replacement/caret mismatch");
        Require(access.Fresh().Command(0x00C7).Result.Success, "native undo");
        Require((bool)target.Invoke(() => target.Editor.Text == "abcd"), "native undo mismatch");
        Require(access.Fresh().Command(0x00B1, 2, 2).Result.Success, "native caret");
        Require(access.Fresh().ReplaceSelection("\r\n").Result.Success, "native multiline insert");
        Require((bool)target.Invoke(() => target.Editor.Text == "ab\r\ncd"), "native multiline mismatch");
        Print(new { route = "NativeEdit", result = "Passed", coverage = "mid-caret insert, Chinese selection replacement, multiline, undo", applicability = "Recognized Edit only; not Chromium" });

        var button = (nint)target.Invoke(() => target.Counter.Handle);
        var iid = new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");
        var hr = Native.AccessibleObjectFromWindow(button, 0xFFFFFFFC, ref iid, out var accessible);
        if (hr < 0 || accessible is null)
        {
            Print(new { route = "MSAA", result = "Unavailable", hresult = hr });
            return;
        }
        try
        {
            Require(Guard().Success, "MSAA pre-guard");
            var before = (int)target.Invoke(() => target.Clicks);
            accessible.accDoDefaultAction(0);
            Thread.Sleep(100);
            Require(Guard().Success, "MSAA changed foreground");
            Require((int)target.Invoke(() => target.Clicks) == before + 1, "MSAA did not invoke once");
            Print(new { route = "MSAA", result = "Passed", coverage = "default action on owned fixture button", applicability = "Provider-specific; not a universal fallback" });
        }
        finally { if (Marshal.IsComObject(accessible)) Marshal.ReleaseComObject(accessible); }
    }

    private static BackgroundInputDispatchResult Await(BackgroundAutomationText automation, BackgroundInputDispatchResult submitted)
    {
        if (submitted.PendingOperation == 0) return submitted;
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(3))
        {
            foreach (var result in automation.DrainCompletions())
                if (result.Operation == submitted.PendingOperation) return result.Dispatch;
            Thread.Sleep(5);
        }
        throw new TimeoutException("No operation completion.");
    }

    private static int Inspect(nint hwnd)
    {
        // Read-only capability discovery. Never print Name, Value, Text or URL.
        var type = Type.GetTypeFromCLSID(new Guid("ff48dba4-60ef-4201-aa87-54103eef594e"), true)!;
        var automation = (IUIAutomation)Activator.CreateInstance(type)!;
        var cache = automation.CreateCacheRequest();
        foreach (var id in new[] { 30003, 30010, 30019, 30020, 30022 }) cache.AddProperty(id);
        foreach (var pattern in new[] { 10000, 10002, 10005, 10010, 10014, 10015 }) cache.AddPattern(pattern);
        cache.put_TreeScope(1);
        var root = automation.ElementFromHandleBuildCache(hwnd, cache);
        var walker = automation.get_RawViewWalker();
        Print(new { result = "RootAvailable", controlType = root.GetCachedPropertyValue(30003), enabled = root.GetCachedPropertyValue(30010), foreground = Native.GetForegroundWindow().ToInt64(), target = hwnd.ToInt64(), note = "Use point inspection in the next round; no writes attempted" });
        var editCondition = automation.CreatePropertyCondition(30003, 50004);
        var edit = root.FindFirstBuildCache(4, editCondition, cache);
        if (edit is not null && edit.GetCachedPropertyValue(30019) is not true)
        {
            var valueObject = edit.GetCachedPattern(10002);
            var textObject = edit.GetCachedPattern(10014);
            var trueCondition = automation.CreateTrueCondition();
            var child = edit.FindFirstBuildCache(4, trueCondition, cache);
            Print(new { result = "EditorCapabilities", writableValue = valueObject is IUIAutomationValuePattern value && !value.get_CurrentIsReadOnly(), textPattern = textObject is IUIAutomationTextPattern, hasDescendants = child is not null, offscreen = edit.GetCachedPropertyValue(30022), note = "No Value/Text content read and no writes attempted" });
            foreach (var item in new object?[] { valueObject, textObject, trueCondition, child, edit })
                if (item is not null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
        }
        Marshal.ReleaseComObject(editCondition);
        Marshal.ReleaseComObject(walker); Marshal.ReleaseComObject(root); Marshal.ReleaseComObject(cache); Marshal.ReleaseComObject(automation);
        return 0;
    }

    private static void Require(bool condition, string failure) { if (!condition) throw new InvalidOperationException(failure); }
    private static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value));
    private sealed class ProbeLogger : ILogger
    {
        public void Log(LogLevel level, string message, Exception? exception = null) => Print(new { level = level.ToString(), message });
    }
    private class ObserverForm : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var value = base.CreateParams; value.ExStyle |= 0x08000000; return value; }
        }
    }
    private sealed class TargetForm : ObserverForm
    {
        public TextBox Editor { get; } = new() { Multiline = true, Bounds = new(20, 20, 320, 80), AccessibleName = "Probe editor" };
        public Button Counter { get; } = new() { Text = "Invoke", Bounds = new(20, 120, 150, 40) };
        public CheckBox Toggle { get; } = new() { Text = "Toggle", Bounds = new(20, 180, 150, 40) };
        public ListBox List { get; } = new() { Bounds = new(20, 240, 320, 70) };
        public TreeView Tree { get; } = new() { Bounds = new(20, 340, 320, 120) };
        public int Clicks { get; private set; }
        public TargetForm()
        {
            Text = "GhostSlacking probe B"; StartPosition = FormStartPosition.Manual; Bounds = new(50, 50, 450, 550); TopMost = true;
            List.Items.AddRange(["First", "Second", "Third"]);
            Tree.Nodes.Add(new TreeNode("Expandable", [new TreeNode("Child")]));
            Controls.AddRange([Editor, Counter, Toggle, List, Tree]);
            Counter.Controls.Add(new Label { Text = "Nested label", Bounds = new(20, 5, 110, 30) });
            Counter.Click += (_, _) => Clicks++;
        }
    }
    private static class Native
    {
        internal delegate void WinEvent(nint hook, uint kind, nint hwnd, int objectId, int childId, uint thread, uint time);
        [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint hwnd);
        [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint first, uint last, nint module, WinEvent callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWinEvent(nint hook);
        [DllImport("oleacc.dll")] internal static extern int AccessibleObjectFromWindow(nint hwnd, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out Accessibility.IAccessible? accessible);
    }
}
