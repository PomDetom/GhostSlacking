using GhostSlacking.Core;
using Microsoft.Win32;
using GhostSlacking.Platform;

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

            if (UserInstallation.IsElevated) return 2;
            using var mutex = new Mutex(false, @"Local\GhostSlacking.Update." + InstallationControlServer.PipeName(installedLocation!));
            try { if (!mutex.WaitOne(0)) return 9; }
            catch (AbandonedMutexException) { }
            try
            {
                using var readyEvent = EventWaitHandle.OpenExisting(options.ReadyEventName);
                using var authorizationEvent = EventWaitHandle.OpenExisting(
                    options.ReadyEventName.Replace(".Ready.", ".Go.", StringComparison.Ordinal));
                return new UpdaterEngine(new WindowsUpdaterRuntime(), new UpdateCompletionStore(logger: logger), logger)
                    .Run(options, () => { readyEvent.Set(); return authorizationEvent.WaitOne(TimeSpan.FromSeconds(5)); });
            }
            finally { mutex.ReleaseMutex(); }
        }
        catch (Exception exception)
        {
            logger.Log(LogLevel.Error, "The automatic updater terminated unexpectedly.", exception);
            return 3;
        }
    }

    private static string? GetInstalledLocation()
    {
        return UserInstallation.Location();
    }
}
