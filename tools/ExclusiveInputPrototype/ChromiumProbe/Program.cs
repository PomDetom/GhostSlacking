using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using GhostSlacking.Core;
using GhostSlacking.Platform;

// A headed, disposable Chromium on a private Windows desktop. CDP only creates
// and inspects our blank fixture. All tested actions use the injected queue.
internal static class Program
{
    private const string Library = "GhostSlacking.InputPrototype.MenuHover.dll";
    private static readonly byte[] Keys = new byte[256];
    private static readonly BackgroundInputOwnership Ownership = new();
    private static int _serial;
    private static bool _attached;
    private static nint _root;
    private static nint _desktop;
    private static int _originX, _originY;
    private static Win32WindowApi? _windows;
    private static RecoveryManager? _recovery;
    private static WindowSnapshot? _snapshot;
    private static Win32RevealMenuHost? _menuVisuals;
    private static OverlayFixture? _overlay;
    private static readonly CancellationTokenSource Deadline = new(TimeSpan.FromSeconds(35));

    private static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] is not ("--browser" or "--worker")) return 2;
        if (args[0] == "--browser") return Isolate(args[1]);
        Console.SetOut(new StreamWriter(Path.Combine(AppContext.BaseDirectory, "chromium-probe.log"), false) { AutoFlush = true });
        try { return Run(args[1]).GetAwaiter().GetResult(); }
        catch (Exception error) { Console.WriteLine($"FAIL {error.GetType().Name}: {error.Message}"); return 1; }
    }

    private static int Isolate(string browser)
    {
        var desktopName = "GhostSlacking.ChromiumInput." + Guid.NewGuid().ToString("N");
        var desktop = CreateDesktopW(desktopName, 0, 0, 0, 0xCF, 0); // no SWITCHDESKTOP access
        if (desktop == 0) throw new System.ComponentModel.Win32Exception();
        try
        {
            var executable = Environment.ProcessPath!;
            var info = Launch(executable, ["--worker", Path.GetFullPath(browser)], desktopName);
            try
            {
                if (WaitForSingleObject(info.Process, 40000) != 0) return 3;
                GetExitCodeProcess(info.Process, out var code);
                var log = Path.Combine(AppContext.BaseDirectory, "chromium-probe.log");
                if (File.Exists(log)) Console.Write(File.ReadAllText(log));
                return (int)code;
            }
            finally { CloseHandle(info.Process); CloseHandle(info.Thread); }
        }
        finally { CloseDesktop(desktop); }
    }

    private static async Task<int> Run(string browser)
    {
        _desktop = GetThreadDesktop(GetCurrentThreadId());
        SetProcessDpiAwarenessContext((nint)(-4));
        BindDesktop();
        var profile = Path.Combine(AppContext.BaseDirectory, "profiles", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        var desktopName = new StringBuilder(256);
        if (!GetUserObjectInformationW(GetThreadDesktop(GetCurrentThreadId()), 2, desktopName, 512, out _)) throw new IOException("No fixture desktop.");
        var process = Launch(browser, ["--remote-debugging-port=0", "--remote-debugging-address=127.0.0.1", "--user-data-dir=" + profile,
            "--no-first-run", "--no-default-browser-check", "--disable-gpu", "--disable-background-networking",
            "--disable-backgrounding-occluded-windows", "--disable-renderer-backgrounding", "--disable-background-timer-throttling",
            "--lang=en-US", "--window-size=1000,760", "about:blank"], desktopName.ToString());
        using var socket = new ClientWebSocket();
        ProcessInfo foregroundProcess = default;
        nint foregroundFixture = 0;
        try
        {
            var portFile = Path.Combine(profile, "DevToolsActivePort");
            string? port = null;
            while (port is null)
            {
                // Chrome creates this before completing its exclusive write.
                // Wait for complete startup data within the worker deadline.
                try
                {
                    var lines = await File.ReadAllLinesAsync(portFile, Deadline.Token);
                    if (lines.Length > 0 && ushort.TryParse(lines[0], out var number) && number != 0) port = lines[0];
                }
                catch (IOException) { }
                if (port is null) await Task.Delay(40, Deadline.Token);
            }
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var pages = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json/list", Deadline.Token));
            var page = pages.RootElement.EnumerateArray().First(value => value.GetProperty("type").GetString() == "page" && value.GetProperty("url").GetString() == "about:blank");
            await socket.ConnectAsync(new Uri(page.GetProperty("webSocketDebuggerUrl").GetString()!), Deadline.Token);
            await Evaluate(socket, "document.body.innerHTML='<div id=editor contenteditable=true style=\"font:24px monospace;margin:30px;width:600px;height:100px;border:1px solid\"></div><p id=words style=\"font:24px monospace;margin:30px;width:650px\">abcdefghij klmnopqrst uvwxyz</p><button id=button style=\"margin:30px;width:120px;height:40px\">fixture</button>';window.clicks=0;window.copies=0;window.ctrlV=0;button.onclick=()=>clicks++;document.addEventListener('copy',e=>{e.preventDefault();copies++});document.addEventListener('keydown',e=>{if(e.ctrlKey&&e.code==='KeyV'){e.preventDefault();ctrlV++}});true");
            uint pid = 0;
            while (_root == 0)
            {
                BindDesktop();
                EnumWindows((hwnd, _) =>
                {
                    GetWindowThreadProcessId(hwnd, out var owner);
                    if (owner != process.Pid) return true;
                    var name = new StringBuilder(128); GetClassNameW(hwnd, name, name.Capacity);
                    if (name.ToString() == "Chrome_WidgetWin_1" && IsWindowVisible(hwnd) && GetWindowRect(hwnd, out var rect) && rect.Right - rect.Left > 500)
                    { _root = hwnd; GetWindowThreadProcessId(hwnd, out pid); return false; }
                    return true;
                }, 0);
                if (_root == 0) await Task.Delay(40, Deadline.Token);
            }
            var metrics = await Evaluate(socket, "({width:innerWidth,height:innerHeight,dpr:devicePixelRatio})");
            var origin = new Point(); ClientToScreen(_root, ref origin); GetClientRect(_root, out var client);
            _originX = origin.X + (client.Right - (int)Math.Round(metrics.GetProperty("width").GetDouble() * metrics.GetProperty("dpr").GetDouble())) / 2;
            _originY = origin.Y + client.Bottom - (int)Math.Round(metrics.GetProperty("height").GetDouble() * metrics.GetProperty("dpr").GetDouble());
            foregroundProcess = Launch(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "BridgeFixture.exe")), ["--foreground"], desktopName.ToString());
            for (var attempt = 0; attempt < 100 && foregroundFixture == 0; attempt++)
            {
                BindDesktop();
                EnumWindows((hwnd, _) =>
                {
                    GetWindowThreadProcessId(hwnd, out var candidate);
                    if (candidate == foregroundProcess.Pid && IsWindowVisible(hwnd)) { foregroundFixture = hwnd; return false; }
                    return true;
                }, 0);
                if (foregroundFixture == 0) await Task.Delay(20, Deadline.Token);
            }
            Check(foregroundFixture != 0, "disposable-A-window-created");
            BindDesktop();
            SetWindowPos(foregroundFixture, 0, 1450, 800, 300, 160, 0x0014);
            await Evaluate(socket, "new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve(true))))");
            Console.WriteLine($"background-desktop-foreground-matches-A={GetForegroundWindow() == foregroundFixture}");
            var foreground = GetForegroundWindow();
            _windows = new Win32WindowApi();
            var visibility = new Win32VisibilityBackend();
            _recovery = new RecoveryManager(_windows, visibility);
            var registered = _recovery.CaptureAndRegister(new TargetWindow
            {
                Hwnd = _root, ProcessId = pid, ScreenBounds = _windows.Observe(_root)!.ScreenBounds
            }, new RevealSettings());
            Check(registered.Success, "root-snapshot-registered-before-injection");
            _snapshot = registered.Value!.Original;
            _menuVisuals = new(_windows, _recovery, visibility);
            Check(visibility.ApplyGhost(_root, _snapshot).Success, "root-uses-production-ghost-styles");
            SetWindowLongPtrW(_root, -20, (nint)((long)GetWindowLongPtrW(_root, -20) | 0x08000000L));
            Check(visibility.ApplyReveal(_root, new CircleRegion(500, 380, 2000, RevealShape.Rectangle), _snapshot).Success,
                "root-uses-nonactivating-production-reveal-path");
            _overlay = new OverlayFixture(_root, _windows.Observe(_root)!.ScreenBounds);
            GetWindowThreadProcessId(_overlay.Handle, out var overlayPid);
            Check(overlayPid == Environment.ProcessId && overlayPid != pid, "overlay-belongs-to-host-process-not-target");
            using var target = Process.GetProcessById((int)pid);
            var startError = Begin(_root, foreground, pid, (ulong)target.StartTime.ToUniversalTime().ToFileTimeUtc());
            Console.WriteLine($"bridge-start-error={startError}");
            Check(startError == 0, "headed-chromium-bridge-starts");
            _attached = true;
            Ownership.Begin([], []);
            Check(Ownership.Activate(), "host-input-ownership-activated");
            var editor = await Location(socket, "editor", 0.1);
            Mouse(0x0200, 0, editor.X, editor.Y);
            Check(Flush(1500) == 0, "initial-hover-cursor-path-before-first-click");
            Click(editor.X, editor.Y);
            Check(Flush(1500) == 0, "first-click-queue-drains");
            await Until(socket, "document.activeElement.id==='editor'", "first-click-focuses-editor");
            foreach (var key in new[] { 'A', 'B', 'C', 'D' }) { Key(key, true); Key(key, false); }
            Check(Flush(1500) == 0, "typing-queue-drains");
            await Until(socket, "editor.textContent==='abcd'", "chromium-inserts-each-character-once");
            Key(0x11, true); Key('A', true); Key('A', false); Key(0x11, false);
            Check(Flush(1500) == 0, "select-all-queue-drains");
            await Until(socket, "getSelection().toString()==='abcd'", "ctrl-a-selects-editor-text");
            Key(0x11, true); Key('C', true); Key('C', false); Key(0x11, false);
            Check(Flush(1500) == 0, "copy-queue-drains");
            await Until(socket, "copies===1", "ctrl-c-dispatches-one-copy-event-clipboard-unchanged");
            Key(0x11, true); Key('V', true); Key('V', false); Key(0x11, false);
            Check(Flush(1500) == 0, "paste-shortcut-queue-drains");
            await Until(socket, "ctrlV===1", "ctrl-v-reaches-renderer-default-paste-cancelled");
            var words = await Location(socket, "words", 0.02);
            Mouse(0x0201, 1, words.X, words.Y);
            for (var step = 1; step <= 15; step++) Mouse(0x0200, 1, words.X + step * 8, words.Y);
            Mouse(0x0202, 0, words.X + 120, words.Y);
            Check(Flush(1500) == 0, "drag-queue-drains");
            await Until(socket, "getSelection().toString().length>=4", "mouse-drag-selects-rendered-text");
            await TestMenus(socket, pid, editor);
            Check(GetForegroundWindow() == foreground, "system-foreground-samples-unchanged");
            End(); _attached = false;
            Console.WriteLine("Boundary: disposable headed Chromium on a background desktop; visible Reveal, browser A events and default clipboard paste remain manual checks.");
            return 0;
        }
        finally
        {
            if (_attached) { End(); _attached = false; }
            _overlay?.Dispose(); _overlay = null;
            try { if (socket.State == WebSocketState.Open) await Call(socket, "Browser.close", new { }); } catch { }
            BindDesktop();
            if (_root == 0 && WaitForSingleObject(process.Process, 0) == 0x102)
                EnumWindows((window, _) =>
                {
                    GetWindowThreadProcessId(window, out var candidate);
                    if (candidate != process.Pid) return true;
                    var name = new StringBuilder(128); GetClassNameW(window, name, name.Capacity);
                    if (name.ToString() == "Chrome_WidgetWin_1") { _root = window; return false; }
                    return true;
                }, 0);
            if (_root != 0 && WaitForSingleObject(process.Process, 0) == 0x102 &&
                GetWindowThreadProcessId(_root, out var rootPid) != 0 && rootPid == process.Pid)
                PostMessageW(_root, 0x0010, 0, 0); // only our still-running, newly created browser
            if (foregroundFixture != 0 && WaitForSingleObject(foregroundProcess.Process, 0) == 0x102 &&
                GetWindowThreadProcessId(foregroundFixture, out var foregroundPid) != 0 && foregroundPid == foregroundProcess.Pid)
                PostMessageW(foregroundFixture, 0x0010, 0, 0);
            if (foregroundProcess.Process != 0) { CloseHandle(foregroundProcess.Thread); CloseHandle(foregroundProcess.Process); }
            CloseHandle(process.Thread); CloseHandle(process.Process);
        }
    }

    private static void Check(bool result, string name)
    {
        Console.WriteLine((result ? "PASS " : "FAIL ") + name);
        if (!result) throw new InvalidOperationException(name);
    }
    private static async Task TestMenus(ClientWebSocket socket, uint pid, Point editor)
    {
        editor.X -= 45; // Right-click inside the four selected fixture characters.
        Click(editor.X, editor.Y);
        Key(0x11, true); Key('A', true); Key('A', false); Key(0x11, false);
        Check(Flush(1500) == 0, "selection-before-context-menu");
        Mouse(0x0204, 2, editor.X, editor.Y); Mouse(0x0205, 0, editor.X, editor.Y);
        Check(Flush(1500) == 0, "context-menu-opening-drains");
        var menu = await WaitMenu(editor.X + 20, editor.Y + 30);
        Check(IsMenu(menu), "owned-context-menu-recognized");
        var items = MenuItems(menu, pid);
        Check(items.Count > 0, "own-menu-items-discovered-read-only");
        var copy = items.FirstOrDefault(item => item.Current.IsEnabled && Regex.IsMatch(item.Current.Name.Replace("&", ""), "^(Copy\\b|复制)", RegexOptions.IgnoreCase));
        Check(copy is not null, "copy-item-located-without-invoking-uia");
        var point = ItemPoint(copy!);
        MenuMouse(0x0200, 0, point); MenuMouse(0x0201, 1, point); MenuMouse(0x0202, 0, point);
        Check(Flush(1500) == 0, "context-menu-click-drains");
        await Until(socket, "copies===2", "menu-click-executes-one-copy-event-clipboard-unchanged");
        for (var attempt = 0; attempt < 50 && MenuWindows(pid).Count != 0; attempt++)
        { Heartbeat(); await Task.Delay(20, Deadline.Token); }
        Check(MenuWindows(pid).Count == 0, "first-menu-fully-closed-before-reopening");
        BindDesktop();
        Check(SetWindowPos(_root, 0, 0, 0, 300, 760, 0x0416), "own-browser-resized-without-activation");
        Mouse(0x0204, 2, editor.X, editor.Y); Mouse(0x0205, 0, editor.X, editor.Y);
        Check(Flush(1500) == 0, "second-menu-opening-drains");
        menu = await WaitMenu(editor.X + 20, editor.Y + 30);
        items = MenuItems(menu, pid);
        var submenu = items.FirstOrDefault(item => item.Current.IsEnabled &&
            Regex.IsMatch(item.Current.Name, "Writing direction|书写方向|文字方向", RegexOptions.IgnoreCase));
        Check(submenu is not null, "submenu-parent-located-read-only");
        point = ItemPoint(submenu!);
        MenuMouse(0x0200, 0, point);
        Check(Flush(1500) == 0, "button-free-hover-drains");
        nint child = 0;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            BindDesktop(); Heartbeat();
            GetWindowRect(menu, out var parentBounds);
            child = MenuWindows(pid).FirstOrDefault(window => window != menu && GetWindowRect(window, out var bounds) &&
                Math.Abs(bounds.Left - parentBounds.Left) > 20);
            if (child != 0) break;
            await Task.Delay(20, Deadline.Token);
        }
        Check(child != 0, "hover-opens-separate-submenu");
        Check(IsMenu(child), "submenu-remains-in-same-session");
        GetWindowRect(child, out var pendingChildBounds);
        ClipMenus(new Point((pendingChildBounds.Left + pendingChildBounds.Right) / 2, (pendingChildBounds.Top + pendingChildBounds.Bottom) / 2));
        Check(Flush(1500) == 0 && IsWindowVisible(child), "submenu-presented-only-after-registered-clip");
        await Task.Delay(100, Deadline.Token); BindDesktop(); Heartbeat();
        GetWindowRect(child, out var childBounds);
        var childItems = MenuItems(child, pid).Concat(MenuItems(menu, pid)).Where(item =>
        {
            var p = ItemPoint(item);
            return p.X >= childBounds.Left && p.X < childBounds.Right && p.Y >= childBounds.Top && p.Y < childBounds.Bottom;
        }).ToList();
        var actionable = childItems.FirstOrDefault(item => item.Current.IsEnabled &&
            Regex.IsMatch(item.Current.Name, "Right[\\s-]*to[\\s-]*left|从右[到向至]左|由右[到向至]左", RegexOptions.IgnoreCase));
        Check(actionable is not null, "submenu-direction-command-located-read-only");
        point = ItemPoint(actionable!);
        point.X = (int)actionable!.Current.BoundingRectangle.Right - 3;
        GetWindowRect(_root, out var rootBounds);
        Check(point.X >= rootBounds.Right, "submenu-command-lies-outside-main-window");
        MenuMouse(0x0200, 0, point); MenuMouse(0x0201, 1, point); MenuMouse(0x0202, 0, point);
        Check(Flush(1500) == 0, "submenu-click-drains");
        Check(!IsWindowVisible(child), "submenu-command-closes-popup");
        await Until(socket, "[editor,...editor.querySelectorAll('*')].some(e=>getComputedStyle(e).direction==='rtl')", "submenu-command-changes-fixture-text-direction");
        for (var attempt = 0; attempt < 50 && MenuWindows(pid).Count != 0; attempt++)
        { Heartbeat(); await Task.Delay(20, Deadline.Token); }
        Mouse(0x0204, 2, editor.X, editor.Y); Mouse(0x0205, 0, editor.X, editor.Y);
        Check(Flush(1500) == 0, "menu-before-escape");
        menu = await WaitMenu(editor.X + 20, editor.Y + 30);
        Key(0x1B, true); Key(0x1B, false);
        Check(Flush(1500) == 0, "escape-menu-queue-drains");
        for (var attempt = 0; attempt < 50 && IsWindowVisible(menu); attempt++)
        { Heartbeat(); await Task.Delay(20, Deadline.Token); }
        BindDesktop();
        Check(!IsWindowVisible(menu), "escape-closes-context-menu-without-ending-session");
        Mouse(0x0204, 2, editor.X, editor.Y); Mouse(0x0205, 0, editor.X, editor.Y);
        Check(Flush(1500) == 0, "menu-before-cancellation");
        menu = await WaitMenu(editor.X + 20, editor.Y + 30);
        var selectAll = MenuItems(menu, pid).FirstOrDefault(item => item.Current.IsEnabled &&
            Regex.IsMatch(item.Current.Name, "Select all|全选", RegexOptions.IgnoreCase));
        Check(selectAll is not null, "cancellation-command-located-read-only");
        var selectionBeforeCancel = (await Evaluate(socket, "getSelection().toString().length")).GetInt32();
        point = ItemPoint(selectAll!);
        MenuMouse(0x0200, 0, point); MenuMouse(0x0201, 1, point);
        Check(Flush(1500) == 0, "pressed-menu-item-before-cancel");
        End(); _attached = false;
        for (var attempt = 0; attempt < 50 && IsWindowVisible(menu); attempt++) await Task.Delay(20, Deadline.Token);
        BindDesktop();
        Check(!IsWindowVisible(menu), "cancel-reveal-closes-owned-menu");
        Check((await Evaluate(socket, "getSelection().toString().length")).GetInt32() == selectionBeforeCancel,
            "cancelling-pressed-menu-item-does-not-execute-command");
    }
    private static List<AutomationElement> MenuItems(nint menu, uint pid)
    {
        BindDesktop();
        return AutomationElement.FromHandle(menu).FindAll(TreeScope.Descendants,
            new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.RadioButton),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox)))
            .Cast<AutomationElement>().Where(item => item.Current.ProcessId == pid).ToList();
    }
    private static Point ItemPoint(AutomationElement item)
    {
        var bounds = item.Current.BoundingRectangle;
        return new((int)(bounds.Left + bounds.Width / 2), (int)(bounds.Top + bounds.Height / 2));
    }
    private static void MenuMouse(uint message, int flags, Point point)
    {
        BindDesktop();
        RouteMouse(message);
        ClipMenus(point);
        var recipient = MenuAt(point.X, point.Y);
        Check(recipient != 0, "live-menu-hit-test");
        var local = point; ScreenToClient(recipient, ref local);
        Check(Send(recipient, message, flags, (nint)(((uint)(ushort)local.Y << 16) | (ushort)local.X), Keys, point.X, point.Y) == 0, "menu-input-admission");
    }
    private static void ClipMenus(Point point)
    {
        var handles = new nint[64];
        var count = GetMenus(handles, (uint)handles.Length);
        Check(count > 0 && count <= handles.Length, "live-menu-list-within-capacity");
        var menus = handles[..(int)count];
        var bounds = _windows!.Observe(_root)!.ScreenBounds;
        foreach (var menu in menus)
            if (_windows.Observe(menu) is { } observed) bounds = System.Drawing.Rectangle.Union(bounds, observed.ScreenBounds);
        var region = RevealGeometry.CreateReveal(bounds, new System.Drawing.Point(point.X, point.Y),
            new RevealSettings { DiameterPx = 96 });
        var visual = RevealGeometry.CreateVisualState(_root, bounds, region, 0, RevealBlurLevel.Low);
        Check(_menuVisuals!.Synchronize(_snapshot!, visual, menus, IsMenu).Success, "menus-clipped-through-registered-platform-host");
        foreach (var menu in menus)
        {
            Check(_recovery!.Find(menu) is not null, "complete-menu-snapshot-retained-for-watchdog");
            var probe = CreateRectRgn(0, 0, 0, 0);
            try
            {
                Check(GetWindowRgn(menu, probe) > 0, "menu-has-reveal-region");
                GetRgnBox(probe, out var box);
                Check(box.Right - box.Left <= 97 && box.Bottom - box.Top <= 97, "menu-region-cannot-exceed-reveal-aperture");
            }
            finally { DeleteObject(probe); }
        }
        var ready = _menuVisuals.ReadyMenus.ToArray();
        _overlay!.Raise();
        var presented = PresentMenus(ready, (uint)ready.Length);
        var drained = Flush(1500);
        Console.WriteLine($"menu-hover-error={presented} drain={drained} stage={ErrorStage()}");
        Check(presented == 0 && drained == 0, "registered-menu-presented-without-activation");
        foreach (var menu in ready)
        {
            var below = false;
            for (var above = GetWindow(menu, 3); above != 0; above = GetWindow(above, 3))
                if (above == _overlay.Handle) { below = true; break; }
            Check(below, "menu-stays-below-host-overlay-without-target-referencing-it");
            var probe = CreateRectRgn(0, 0, 0, 0);
            try
            {
                Check(GetWindowRgn(menu, probe) > 0, "menu-retains-region-after-show-callbacks");
                GetRgnBox(probe, out var box);
                Check(box.Right - box.Left <= 97 && box.Bottom - box.Top <= 97, "shown-menu-stays-inside-reveal-aperture");
            }
            finally { DeleteObject(probe); }
        }
    }
    private static async Task<nint> WaitMenu(int x, int y)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            BindDesktop(); Heartbeat();
            var menu = MenuWindows(_snapshot!.ProcessId).FirstOrDefault(window =>
                GetWindowRect(window, out var rect) && x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom);
            if (menu != 0)
            {
                Check(!IsWindowVisible(menu), "new-menu-is-hidden-until-snapshot-and-region-ready");
                ClipMenus(new Point(x, y));
                Check(IsWindowVisible(menu), "clipped-menu-is-now-visible");
                return menu;
            }
            await Task.Delay(20, Deadline.Token);
        }
        throw new InvalidOperationException("No owned menu at fixture point.");
    }
    private static List<nint> MenuWindows(uint pid)
    {
        BindDesktop();
        var menus = new List<nint>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == pid && IsMenu(window)) menus.Add(window);
            return true;
        }, 0);
        return menus;
    }
    private static async Task Until(ClientWebSocket socket, string expression, string name)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if ((await Evaluate(socket, expression)).GetBoolean()) { Check(true, name); return; }
            await Task.Delay(30, Deadline.Token);
        }
        Check(false, name);
    }
    private static async Task<Point> Location(ClientWebSocket socket, string id, double fraction)
    {
        var point = await Evaluate(socket, $"(()=>{{let r=document.getElementById('{id}').getBoundingClientRect();return {{x:(r.x+r.width*{fraction.ToString(System.Globalization.CultureInfo.InvariantCulture)})*devicePixelRatio,y:(r.y+20)*devicePixelRatio}}}})()");
        return new(_originX + (int)Math.Round(point.GetProperty("x").GetDouble()), _originY + (int)Math.Round(point.GetProperty("y").GetDouble()));
    }
    private static void Mouse(uint message, int flags, int x, int y)
    {
        BindDesktop();
        RouteMouse(message);
        var client = new Point(x, y); ScreenToClient(_root, ref client);
        var parameter = (nint)(((uint)(ushort)client.Y << 16) | (ushort)client.X);
        Check(Send(_root, message, flags, parameter, Keys, x, y) == 0, "mouse-queue-admission");
    }
    private static void Click(int x, int y) { Mouse(0x0201, 1, x, y); Mouse(0x0202, 0, x, y); }
    private static void RouteMouse(uint message)
    {
        BackgroundMouseButton? button = message switch
        {
            0x0201 or 0x0202 => BackgroundMouseButton.Left,
            0x0204 or 0x0205 => BackgroundMouseButton.Right,
            _ => null
        };
        var move = message == 0x0200;
        Check(Ownership.Mouse(button, message is 0x0202 or 0x0205, move, forwardHover: true) == BackgroundInputRoute.Deliver,
            move && !Ownership.HasMouseDown ? "host-routes-button-free-hover-without-another-click" : "host-routes-mouse-input");
    }
    private static void Key(int key, bool down)
    {
        BindDesktop();
        Keys[key] = down ? (byte)0x80 : (byte)0;
        if (key == 0x11) Keys[0xA2] = Keys[key];
        var scan = MapVirtualKeyW((uint)key, 0);
        var parameter = 1U | (scan << 16) | (down ? 0U : 0xC0000000U);
        Check(Send(_root, down ? 0x0100U : 0x0101U, key, (nint)parameter, Keys, _originX + 100, _originY + 50) == 0, "key-queue-admission");
    }
    private static async Task<JsonElement> Evaluate(ClientWebSocket socket, string expression)
    {
        var response = await Call(socket, "Runtime.evaluate", new { expression, returnByValue = true, awaitPromise = true });
        if (response.TryGetProperty("exceptionDetails", out _)) throw new InvalidOperationException("Fixture evaluation failed.");
        return response.GetProperty("result").GetProperty("value").Clone();
    }
    private static async Task<JsonElement> Call(ClientWebSocket socket, string method, object parameters)
    {
        BindDesktop();
        if (_attached && Heartbeat() != 0) throw new InvalidOperationException("Bridge lease or queue failed.");
        var id = ++_serial;
        await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id, method, @params = parameters })), WebSocketMessageType.Text, true, Deadline.Token);
        while (true)
        {
            using var stream = new MemoryStream();
            var buffer = new byte[8192]; WebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(buffer, Deadline.Token);
                if (received.MessageType == WebSocketMessageType.Close) throw new IOException("Fixture browser closed.");
                stream.Write(buffer, 0, received.Count);
                if (stream.Length > 1024 * 1024) throw new IOException("Oversized fixture result.");
            } while (!received.EndOfMessage);
            using var response = JsonDocument.Parse(stream.ToArray());
            if (!response.RootElement.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id) continue;
            if (response.RootElement.TryGetProperty("error", out var error)) throw new IOException("Protocol error " + error.GetProperty("code").GetInt32());
            BindDesktop();
            return response.RootElement.GetProperty("result").Clone();
        }
    }

    private static string Quote(string value) => "\"" + Regex.Replace(Regex.Replace(value, "(\\\\*)\"", "$1$1\\\""), "(\\\\+)$", "$1$1") + "\"";
    private static ProcessInfo Launch(string executable, string[] args, string desktop)
    {
        var startup = new Startup { Size = Marshal.SizeOf<Startup>(), Desktop = desktop };
        var command = new StringBuilder(Quote(executable) + " " + string.Join(" ", args.Select(Quote)));
        if (!CreateProcessW(executable, command, 0, 0, false, 0x08000000, 0, AppContext.BaseDirectory, ref startup, out var process))
            throw new System.ComponentModel.Win32Exception();
        return process;
    }
    private static void BindDesktop()
    {
        // Async continuations can run on a different thread. Never enumerate
        // or hook a window outside the explicitly created fixture desktop.
        if (_desktop != 0 && GetThreadDesktop(GetCurrentThreadId()) != _desktop && !SetThreadDesktop(_desktop)) throw new System.ComponentModel.Win32Exception();
        SetThreadDpiAwarenessContext((nint)(-4));
    }
    private sealed class OverlayFixture : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new(), _stop = new();
        private readonly Thread _thread;
        private Exception? _error;
        public nint Handle { get; private set; }
        public OverlayFixture(nint owner, System.Drawing.Rectangle bounds)
        {
            _thread = new Thread(() =>
            {
                try
                {
                    BindDesktop();
                    using var overlay = new Win32OverlayWindow(owner);
                    if (!overlay.SetBounds(bounds).Success ||
                        !overlay.SetRingRegion(new CircleRegion(60, 60, 160), new CircleRegion(60, 60, 96)).Success ||
                        !overlay.ShowTopmostNoActivate().Success) throw new IOException("Could not create disposable overlay.");
                    Handle = overlay.Handle; _ready.Set();
                    while (!_stop.IsSet)
                    {
                        while (PeekMessageW(out var message, 0, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessageW(ref message); }
                        _stop.Wait(1);
                    }
                }
                catch (Exception error) { _error = error; _ready.Set(); }
            }) { IsBackground = true, Name = "Disposable reveal overlay" };
            _thread.Start();
            if (!_ready.Wait(3000)) throw new IOException("Overlay fixture did not start.");
            if (_error is not null) throw new IOException("Overlay fixture failed.", _error);
        }
        public void Dispose() { _stop.Set(); _thread.Join(2000); }
        public void Raise()
        {
            if (!SetWindowPos(Handle, (nint)(-1), 0, 0, 0, 0, 0x0213)) throw new IOException("Could not raise host overlay.");
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeMessage
    {
        public nint Window; public uint Id; public nint Wparam, Lparam; public uint Time; public Point Point; public uint Private;
    }
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out NativeMessage message, nint window, uint first, uint last, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref NativeMessage message);
    [StructLayout(LayoutKind.Sequential)] private struct Point(int x = 0, int y = 0) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Startup
    {
        public int Size; public string? Reserved, Desktop, Title;
        public int X, Y, Width, Height, XChars, YChars, Fill, Flags; public short Show, ReservedSize;
        public nint ReservedPointer, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public nint Process, Thread; public uint Pid, Tid; }
    private delegate bool EnumWindow(nint hwnd, nint parameter);
    [DllImport(Library, EntryPoint = "GS_Begin", CallingConvention = CallingConvention.Cdecl)] private static extern uint Begin(nint root, nint foreground, uint pid, ulong start);
    [DllImport(Library, EntryPoint = "GS_Dispatch", CallingConvention = CallingConvention.Cdecl)] private static extern uint Send(nint recipient, uint message, nint wparam, nint lparam, byte[] keys, int x, int y);
    [DllImport(Library, EntryPoint = "GS_Heartbeat", CallingConvention = CallingConvention.Cdecl)] private static extern uint Heartbeat();
    [DllImport(Library, EntryPoint = "GS_Flush", CallingConvention = CallingConvention.Cdecl)] private static extern uint Flush(uint timeout);
    [DllImport(Library, EntryPoint = "GS_End", CallingConvention = CallingConvention.Cdecl)] private static extern void End();
    [DllImport(Library, EntryPoint = "GS_MenuAt", CallingConvention = CallingConvention.Cdecl)] private static extern nint MenuAt(int x, int y);
    [DllImport(Library, EntryPoint = "GS_IsMenu", CallingConvention = CallingConvention.Cdecl)] private static extern bool IsMenu(nint window);
    [DllImport(Library, EntryPoint = "GS_GetMenus", CallingConvention = CallingConvention.Cdecl)] private static extern uint GetMenus([Out] nint[] windows, uint capacity);
    [DllImport(Library, EntryPoint = "GS_PresentMenus", CallingConvention = CallingConvention.Cdecl)] private static extern uint PresentMenus([In] nint[] menus, uint count);
    [DllImport(Library, EntryPoint = "GS_ErrorStage", CallingConvention = CallingConvention.Cdecl)] private static extern uint ErrorStage();
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int GetRgnBox(nint region, out Rect bounds);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint region);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateDesktopW(string name, nint device, nint mode, uint flags, uint access, nint attributes);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessW(string app, StringBuilder command, nint pa, nint ta, bool inherit, uint flags, nint environment, string directory, ref Startup startup, out ProcessInfo process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle, uint time);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(nint process, out uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern nint GetThreadDesktop(uint thread);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetThreadDesktop(nint desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformationW(nint handle, int index, StringBuilder value, uint length, out uint needed);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint hwnd, StringBuilder value, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyW(uint key, uint mode);
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint hwnd, uint message, nint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint window, int index, nint value);
}
