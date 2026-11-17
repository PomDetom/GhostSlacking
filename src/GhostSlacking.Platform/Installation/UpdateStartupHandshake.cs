using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace GhostSlacking.Platform;

public static class UpdateStartupHandshake
{
    private const string Prefix = "GhostSlacking.UpdateStartup.";

    public static bool StartAndWait(string applicationPath, string version, Action confirmed)
    {
        var session = Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(Prefix + session, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var info = new ProcessStartInfo(applicationPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(applicationPath)
        };
        info.ArgumentList.Add("--update-session");
        info.ArgumentList.Add(session);
        using var process = Process.Start(info);
        if (process is null) return false;
        try
        {
            pipe.WaitForConnectionAsync(cancellation.Token).GetAwaiter().GetResult();
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var clientPid) ||
                clientPid != process.Id) return false;
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, leaveOpen: true);
            if (reader.ReadLineAsync(cancellation.Token).AsTask().GetAwaiter().GetResult() != $"READY {session} {version}") return false;
            confirmed();
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            writer.WriteLine($"CONFIRMED {session}");
            return true;
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            return false;
        }
    }

    public static async Task<bool> ReportAsync(string[] args, string version)
    {
        if (args.Length != 2 || args[0] != "--update-session" ||
            !Guid.TryParseExact(args[1], "N", out var session)) return false;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var pipe = new NamedPipeClientStream(".", Prefix + session.ToString("N"), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(cancellation.Token);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync($"READY {session:N} {version}");
            using var reader = new StreamReader(pipe, new UTF8Encoding(false));
            return await reader.ReadLineAsync(cancellation.Token) == $"CONFIRMED {session:N}";
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException) { return false; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(nint pipe, out int processId);
}
