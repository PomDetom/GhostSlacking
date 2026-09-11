using GhostSlacking.Core;
using Microsoft.Win32;

namespace GhostSlacking.Updater;

internal static class Program
{
    private static int Main(string[] args)
    {
        using var logger = new RollingFileLogger(
            Path.Combine(GhostSlackingDataPaths.LogDirectory, "updater.log"));
        try
        {
            var installedLocation = GetInstalledLocation();
            if (!UpdaterOptions.TryParse(
                    args,
                    Path.Combine(GhostSlackingDataPaths.RootDirectory, "updates"),
                    installedLocation,
                    out var options,
                    out var error))
            {
                logger.Log(LogLevel.Error, $"Updater startup rejected: {error}");
                return 2;
            }

            using var readyEvent = EventWaitHandle.OpenExisting(options.ReadyEventName);
            readyEvent.Set();
            return new UpdaterEngine(new WindowsUpdaterRuntime(), new UpdateCompletionStore(logger: logger), logger)
                .Run(options);
        }
        catch (Exception exception)
        {
            logger.Log(LogLevel.Error, "The automatic updater terminated unexpectedly.", exception);
            return 3;
        }
    }

    private static string? GetInstalledLocation()
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(@"Software\GhostSlacking");
        return key?.GetValue("InstallLocation") as string;
    }
}
