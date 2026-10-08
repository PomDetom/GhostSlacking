using System.Windows;
using WixToolset.BootstrapperApplicationApi;

namespace GhostSlacking.Setup;

internal static class Program
{
    [MTAThread]
    private static int Main(string[] args)
    {
        if (args.Length is >= 2 and <= 5 && args[0] == "--preview")
        {
            var previewThread = new Thread(() =>
            {
                var percent = args.Length >= 3 ? int.Parse(args[2]) : 47;
                var dpi = args.Length >= 4 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 96;
                var window = new InstallerWindow("C:\\Program Files\\GhostSlacking") { ShowActivated = false };
                if (args.Length == 5)
                {
                    window.ConfigureReady("安装", freshInstall: true, manual: true, uninstall: false);
                    if (args[4] == "expanded") window.FreshInstallOptions.IsExpanded = true;
                }
                window.SetProgress(percent, $"正在安装 · {percent}%");
                window.SavePreview(args[1], dpi);
            });
            previewThread.SetApartmentState(ApartmentState.STA);
            previewThread.Start(); previewThread.Join();
            return 0;
        }
        ManagedBootstrapperApplication.Run(new InstallerApplication());
        return 0;
    }
}
