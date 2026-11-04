using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace GhostSlacking.Platform;

// Owns the hook message pump and the absolute drain deadline, independently of
// Avalonia rendering and slow accessibility providers. Never calls target APIs.
internal sealed class BackgroundHookThread : IDisposable
{
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly Thread _thread;
    private volatile bool _stopping;
    public Action? Tick { get; set; }

    public BackgroundHookThread()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Background input hooks" };
        _thread.Start();
    }

    public bool Invoke(Action action)
    {
        if (_stopping) return false;
        if (Thread.CurrentThread == _thread) { action(); return true; }
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = 0;
        Exception? failure = null;
        _commands.Enqueue(() =>
        {
            if (Volatile.Read(ref cancelled) != 0) return;
            try { action(); } catch (Exception error) { failure = error; }
            finally { if (Volatile.Read(ref cancelled) == 0) finished.TrySetResult(); }
        });
        // A timeout does not allow a late install to revive a cancelled session.
        if (!finished.Task.Wait(TimeSpan.FromSeconds(1))) { Interlocked.Exchange(ref cancelled, 1); return false; }
        if (failure is not null) throw new InvalidOperationException("Hook-thread command failed.", failure);
        return true;
    }

    private void Run()
    {
        PeekMessage(out _, 0, 0, 0, 0); // Create the queue before installing any hook.
        while (!_stopping)
        {
            while (_commands.TryDequeue(out var command)) command();
            for (var messages = 0; messages < 64 && PeekMessage(out var message, 0, 0, 0, 1); messages++)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
            Tick?.Invoke();
            Thread.Sleep(10);
        }
    }

    public void Dispose()
    {
        _stopping = true;
        if (Thread.CurrentThread != _thread) _thread.Join(1000);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nuint First;
        public nint Second;
        public uint Time;
        public int X, Y;
        public uint Private;
    }
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out Message message, nint window, uint first, uint last, uint remove);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern nint DispatchMessage(ref Message message);
}
