using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace BackgroundInteractionProbe;

// Isolated Chromium protocol experiment. No connection to real ChatGPT/Codex,
// no user profile reuse, no startup flags changed on installed apps.
internal static class ChromiumResearch
{
    public static async Task<int> Run(string browserPath, string profilePath)
    {
        if (!Path.IsPathFullyQualified(profilePath) || Directory.Exists(profilePath))
            throw new ArgumentException("A new absolute isolated profile path is required.");
        var initialForeground = GetForegroundWindow();
        Directory.CreateDirectory(profilePath);
        using var browser = new Process { StartInfo = new(browserPath) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden } };
        foreach (var arg in new[] { "--headless=new", "--remote-debugging-port=0", "--remote-debugging-address=127.0.0.1", "--user-data-dir=" + profilePath, "--no-first-run", "--no-default-browser-check", "about:blank" })
            browser.StartInfo.ArgumentList.Add(arg);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var launched = false;
        try
        {
            launched = browser.Start();
            var activePort = Path.Combine(profilePath, "DevToolsActivePort");
            while (!File.Exists(activePort)) await Task.Delay(50, deadline.Token);
            var port = int.Parse((await File.ReadAllLinesAsync(activePort, deadline.Token))[0]);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var pages = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json/list", deadline.Token));
            var page = pages.RootElement.EnumerateArray().First(value => value.GetProperty("type").GetString() == "page" && value.GetProperty("url").GetString() == "about:blank");
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri(page.GetProperty("webSocketDebuggerUrl").GetString()!), deadline.Token);
            var id = 0;
            async Task<JsonElement> Call(string method, object parameters)
            {
                if (GetForegroundWindow() != initialForeground) throw new InvalidOperationException("Observed foreground changed; stopping without retry.");
                var serial = ++id;
                await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id = serial, method, @params = parameters })), WebSocketMessageType.Text, true, deadline.Token);
                while (true)
                {
                    using var stream = new MemoryStream();
                    var buffer = new byte[8192];
                    WebSocketReceiveResult received;
                    do
                    {
                        received = await socket.ReceiveAsync(buffer, deadline.Token);
                        if (received.MessageType == WebSocketMessageType.Close) throw new IOException("Protocol connection closed.");
                        stream.Write(buffer, 0, received.Count);
                        if (stream.Length > 1024 * 1024) throw new IOException("Oversized protocol result.");
                    } while (!received.EndOfMessage);
                    using var response = JsonDocument.Parse(stream.ToArray());
                    if (!response.RootElement.TryGetProperty("id", out var responseId) || responseId.GetInt32() != serial) continue;
                    if (response.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException("Protocol error code: " + error.GetProperty("code").GetInt32());
                    if (GetForegroundWindow() != initialForeground) throw new InvalidOperationException("Observed foreground changed after protocol command.");
                    return response.RootElement.GetProperty("result").Clone();
                }
            }
            async Task<JsonElement> Evaluate(string expression)
            {
                var response = await Call("Runtime.evaluate", new { expression, returnByValue = true });
                if (response.TryGetProperty("exceptionDetails", out _)) throw new InvalidOperationException("Fixture evaluation failed.");
                return response.GetProperty("result").GetProperty("value");
            }
            await Evaluate("document.body.innerHTML='<div id=editor contenteditable=true style=\"white-space:pre-wrap;width:400px;height:100px\"></div><button id=counter>counter</button>';window.clicks=0;window.inputs=0;counter.onclick=()=>window.clicks++;editor.oninput=()=>window.inputs++;editor.focus();true");
            await Call("Input.insertText", new { text = "abc中文\nsecond" });
            var multiline = (await Evaluate("editor.innerText==='abc中文\\nsecond' && window.inputs>0")).GetBoolean();
            if (!multiline) throw new InvalidOperationException("Multiline/input-event verification failed.");
            await Evaluate("editor.textContent='abcd';editor.focus();var range=document.createRange();range.setStart(editor.firstChild,1);range.setEnd(editor.firstChild,3);var selection=window.getSelection();selection.removeAllRanges();selection.addRange(range);true");
            await Call("Input.insertText", new { text = "中" });
            var replacement = (await Evaluate("editor.textContent==='a中d'")).GetBoolean();
            var undo = (await Evaluate("document.execCommand('undo');editor.textContent==='abcd'")).GetBoolean();
            var rect = await Evaluate("(()=>{var r=counter.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()");
            var x = rect.GetProperty("x").GetDouble(); var y = rect.GetProperty("y").GetDouble();
            await Call("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", clickCount = 1 });
            await Call("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", clickCount = 1 });
            var click = (await Evaluate("window.clicks===1")).GetBoolean();
            if (!replacement || !undo || !click) throw new InvalidOperationException("Replacement, undo or click verification failed.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                route = "ChromiumCDP", result = "Passed", multiline, inputEvents = true, selectionReplacement = replacement, undo, click,
                foregroundSamplesUnchanged = GetForegroundWindow() == initialForeground,
                applicability = "Isolated headless Chromium only; real target connectivity, visible Reveal and browser-A events remain unverified",
                profile = profilePath
            }));
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { route = "ChromiumCDP", result = "Failed", error = error.GetType().Name, detail = error.Message }));
            return 1;
        }
        finally
        {
            // Only the isolated browser process launched above is stopped.
            if (launched && !browser.HasExited) { browser.Kill(true); await browser.WaitForExitAsync(); }
        }
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
