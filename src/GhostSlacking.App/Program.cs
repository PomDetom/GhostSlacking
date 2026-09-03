using System.Threading;

namespace GhostSlacking.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "Local\\GhostSlacking.SingleInstance", out var created);
        if (!created)
        {
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, args) => MessageBox.Show(
            $"发生未预期的界面错误：{args.Exception.Message}", "GhostSlacking", MessageBoxButtons.OK, MessageBoxIcon.Error);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(exception);
            }
        };

        Application.Run(new GhostApplicationContext());
    }
}
