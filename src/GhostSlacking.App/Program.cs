using Avalonia;
using Avalonia.Controls;
using System.Threading;

namespace GhostSlacking.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "Local\\GhostSlacking.SingleInstance", out var created);
        if (!created)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(exception);
            }
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    internal static AppBuilder BuildAvaloniaApp(Func<GhostSlackingApplication>? createApp = null) =>
        AppBuilder.Configure(createApp ?? (() => new GhostSlackingApplication()))
            .UseWin32()
            .UseSkia();
}
