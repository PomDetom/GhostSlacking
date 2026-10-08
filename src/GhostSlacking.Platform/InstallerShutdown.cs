using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GhostSlacking.Platform;

/// <summary>Closes only processes whose executable is in the registered installation.</summary>
public static class InstallerShutdown
{
    public static void CloseAndWait(string? installDirectory, TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        var processes = new List<Process>();
        try
        {
            foreach (var name in new[] { "GhostSlacking.App", "GhostSlacking.Watchdog" })
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    processes.Add(process);
                    if (process.HasExited) continue;
                    if (string.IsNullOrWhiteSpace(installDirectory) ||
                        !MatchesExecutable(process.MainModule?.FileName, installDirectory, name + ".exe"))
                        throw new InvalidOperationException("无法验证正在运行的 GhostSlacking 安装目录，请先安全退出应用后重试。");
                    // Holding this process handle prevents a reused PID being accepted as the original process.
                    _ = process.Handle;
                    if (name != "GhostSlacking.App") continue;
                    var sent = false;
                    EnumWindows((window, _) =>
                    {
                        GetWindowThreadProcessId(window, out var pid);
                        var className = new StringBuilder(128);
                        GetClassName(window, className, className.Capacity);
                        if (pid == process.Id && className.ToString() == "GhostSlacking.MessageWindow")
                            sent |= PostMessage(window, 0x0010, 0, 0);
                        return true;
                    }, 0);
                    if (!sent && !process.HasExited)
                        throw new InvalidOperationException("未找到安全退出消息窗口，请先退出 GhostSlacking 后重试。");
                }
            }
            foreach (var process in processes)
            {
                var remaining = timeout - deadline.Elapsed;
                if (!process.HasExited && (remaining <= TimeSpan.Zero || !process.WaitForExit(remaining)))
                    throw new InvalidOperationException("GhostSlacking 或 Watchdog 未能安全退出，安装已停止，旧版本未更改。");
            }
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("无法验证或关闭应用进程，请先安全退出 GhostSlacking 后重试。", exception);
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    internal static bool MatchesExecutable(string? executable, string directory, string name) =>
        executable is not null && string.Equals(Path.GetFullPath(executable),
            Path.GetFullPath(Path.Combine(directory, name)), StringComparison.OrdinalIgnoreCase);

    private delegate bool EnumWindowsCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out int processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
