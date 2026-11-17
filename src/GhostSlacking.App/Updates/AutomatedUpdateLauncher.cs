using System.Diagnostics;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal interface IUpdateInstallerLauncher
{
    bool Launch(UpdateInstallRequest request);
}

internal sealed record UpdateInstallRequest(
    string InstallerPath,
    string Version,
    string ApplicationPath,
    int ParentProcessId,
    long ParentStartTimeUtcTicks);

internal sealed class AutomatedUpdateLauncher(
    ILogger logger,
    string? updaterSourceDirectory = null) : IUpdateInstallerLauncher
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(5);
    private readonly string _updaterSourceDirectory = updaterSourceDirectory ??
        Path.Combine(AppContext.BaseDirectory, "Updater");

    public bool Launch(UpdateInstallRequest request)
    {
        var stagingDirectory = Path.Combine(
            Path.GetDirectoryName(request.InstallerPath) ?? throw new InvalidOperationException("The installer directory is unavailable."),
            $"updater-{Guid.NewGuid():N}");
        try
        {
            if (!Directory.Exists(_updaterSourceDirectory))
            {
                throw new DirectoryNotFoundException("The automatic updater files are missing.");
            }

            Directory.CreateDirectory(stagingDirectory);
            foreach (var sourcePath in Directory.EnumerateFiles(_updaterSourceDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                File.Copy(sourcePath, Path.Combine(stagingDirectory, Path.GetFileName(sourcePath)), overwrite: true);
            }

            var executablePath = Path.Combine(stagingDirectory, "GhostSlacking.Updater.exe");
            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException("The automatic updater executable is missing.", executablePath);
            }

            var readyEventName = $@"Local\GhostSlacking.Updater.Ready.{Guid.NewGuid():N}";
            using var readyEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                readyEventName);
            using var authorizationEvent = new EventWaitHandle(false, EventResetMode.AutoReset,
                readyEventName.Replace(".Ready.", ".Go.", StringComparison.Ordinal));
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = stagingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            AddArgument("--parent-pid", request.ParentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AddArgument("--parent-start-ticks", request.ParentStartTimeUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AddArgument("--installer", request.InstallerPath);
            AddArgument("--version", request.Version);
            AddArgument("--application", request.ApplicationPath);
            AddArgument("--ready-event", readyEventName);
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                throw new InvalidOperationException("The automatic updater process did not start.");
            }

            if (readyEvent.WaitOne(ReadyTimeout))
            {
                authorizationEvent.Set();
                return true;
            }

            logger.Log(LogLevel.Warning, "The automatic updater did not acknowledge the handoff.");
            return false;

            void AddArgument(string name, string value)
            {
                startInfo.ArgumentList.Add(name);
                startInfo.ArgumentList.Add(value);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.Log(LogLevel.Warning, "The automatic updater could not be staged or started.", exception);
            TryDeleteDirectory(stagingDirectory);
            return false;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

}
