using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using GhostSlacking.Core;

namespace GhostSlacking.Updater;

internal sealed record UpdaterOptions(
    int ParentProcessId,
    long ParentStartTimeUtcTicks,
    string InstallerPath,
    string Version,
    string ApplicationPath,
    string ReadyEventName)
{
    private const string ReadyEventPrefix = @"Local\GhostSlacking.Updater.Ready.";

    public static bool TryParse(
        string[] args,
        string updatesRoot,
        string? installedLocation,
        out UpdaterOptions options,
        out string error)
    {
        options = null!;
        error = string.Empty;
        if (args.Length != 12 ||
            !TryValue(args, "--parent-pid", out var parentPidText) ||
            !TryValue(args, "--parent-start-ticks", out var parentStartText) ||
            !TryValue(args, "--installer", out var installerPath) ||
            !TryValue(args, "--version", out var versionText) ||
            !TryValue(args, "--application", out var applicationPath) ||
            !TryValue(args, "--ready-event", out var readyEventName))
        {
            error = "Required arguments are missing or duplicated.";
            return false;
        }

        if (!int.TryParse(parentPidText, NumberStyles.None, CultureInfo.InvariantCulture, out var parentPid) ||
            parentPid <= 0 ||
            !long.TryParse(parentStartText, NumberStyles.None, CultureInfo.InvariantCulture, out var parentStartTicks) ||
            parentStartTicks <= 0)
        {
            error = "The parent process identity is invalid.";
            return false;
        }

        if (!TryParseVersion(versionText, out var normalizedVersion))
        {
            error = "The target version is invalid.";
            return false;
        }

        if (!IsValidReadyEventName(readyEventName))
        {
            error = "The updater readiness event is invalid.";
            return false;
        }

        try
        {
            var fullUpdatesRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(updatesRoot)) +
                Path.DirectorySeparatorChar;
            var fullInstallerPath = Path.GetFullPath(installerPath);
            var expectedInstallerName = $"GhostSlacking-{normalizedVersion}-win-x64.msi";
            if (!fullInstallerPath.StartsWith(fullUpdatesRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(fullInstallerPath), expectedInstallerName, StringComparison.Ordinal) ||
                !File.Exists(fullInstallerPath))
            {
                error = "The installer path is outside the validated update cache.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(installedLocation))
            {
                error = "The installed application location is unavailable.";
                return false;
            }

            var fullApplicationPath = Path.GetFullPath(applicationPath);
            var expectedApplicationPath = Path.GetFullPath(
                Path.Combine(installedLocation, "GhostSlacking.App.exe"));
            if (!string.Equals(fullApplicationPath, expectedApplicationPath, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(fullApplicationPath))
            {
                error = "The application path does not match the registered installation.";
                return false;
            }

            options = new UpdaterOptions(
                parentPid,
                parentStartTicks,
                fullInstallerPath,
                normalizedVersion,
                fullApplicationPath,
                readyEventName);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool TryValue(string[] args, string name, out string value)
    {
        value = string.Empty;
        var index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length || Array.LastIndexOf(args, name) != index)
        {
            return false;
        }

        value = args[index + 1];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryParseVersion(string value, out string normalized)
    {
        normalized = string.Empty;
        if (!System.Version.TryParse(value, out var version) || version.Build < 0 || version.Revision >= 0)
        {
            return false;
        }

        normalized = version.ToString(3);
        return string.Equals(normalized, value, StringComparison.Ordinal);
    }

    private static bool IsValidReadyEventName(string value) =>
        value.Length == ReadyEventPrefix.Length + 32 &&
        value.StartsWith(ReadyEventPrefix, StringComparison.Ordinal) &&
        value[ReadyEventPrefix.Length..].All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

internal interface IUpdaterRuntime
{
    bool WaitForParentExit(int processId, long startTimeUtcTicks, TimeSpan timeout);
    int RunInstaller(string installerPath);
    bool StartApplication(string applicationPath);
    void ShowFatalError(string message);
}

internal sealed class UpdaterEngine(
    IUpdaterRuntime runtime,
    UpdateCompletionStore completionStore,
    ILogger logger)
{
    private static readonly TimeSpan ParentExitTimeout = TimeSpan.FromSeconds(30);

    public int Run(UpdaterOptions options)
    {
        if (!runtime.WaitForParentExit(
                options.ParentProcessId,
                options.ParentStartTimeUtcTicks,
                ParentExitTimeout))
        {
            logger.Log(LogLevel.Error, "The main application did not exit before the updater timeout.");
            completionStore.Save(Result(options, UpdateCompletionStatus.Failed, null));
            return 4;
        }

        int installerExitCode;
        try
        {
            installerExitCode = runtime.RunInstaller(options.InstallerPath);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == WindowsUpdaterRuntime.ErrorCancelled)
        {
            logger.Log(LogLevel.Info, "The user cancelled the updater elevation request.");
            return Finish(options, UpdateCompletionStatus.Cancelled, null, 5);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            logger.Log(LogLevel.Error, "Windows Installer could not be started.", exception);
            return Finish(options, UpdateCompletionStatus.Failed, null, 6);
        }

        if (installerExitCode is WindowsUpdaterRuntime.Success or
            WindowsUpdaterRuntime.SuccessRebootInitiated or
            WindowsUpdaterRuntime.SuccessRebootRequired)
        {
            logger.Log(LogLevel.Info, $"Automatic update completed with installer exit code {installerExitCode}.");
            return Finish(options, UpdateCompletionStatus.Succeeded, installerExitCode, 0);
        }

        logger.Log(LogLevel.Error, $"Automatic update failed with installer exit code {installerExitCode}.");
        return Finish(options, UpdateCompletionStatus.Failed, installerExitCode, 7);
    }

    private int Finish(
        UpdaterOptions options,
        UpdateCompletionStatus status,
        int? installerExitCode,
        int updaterExitCode)
    {
        completionStore.Save(Result(options, status, installerExitCode));
        if (runtime.StartApplication(options.ApplicationPath))
        {
            return updaterExitCode;
        }

        logger.Log(LogLevel.Error, "The application could not be restarted after the update attempt.");
        completionStore.Save(Result(options, UpdateCompletionStatus.Failed, installerExitCode));
        runtime.ShowFatalError(
            $"GhostSlacking 无法在更新后自动启动。请手动启动应用并查看日志：{Path.Combine(GhostSlackingDataPaths.LogDirectory, "updater.log")}");
        return 8;
    }

    private static UpdateCompletionResult Result(
        UpdaterOptions options,
        UpdateCompletionStatus status,
        int? installerExitCode) => new(
        options.Version,
        status,
        installerExitCode,
        DateTimeOffset.UtcNow);
}

internal sealed class WindowsUpdaterRuntime : IUpdaterRuntime
{
    internal const int Success = 0;
    internal const int SuccessRebootInitiated = 1641;
    internal const int SuccessRebootRequired = 3010;
    internal const int ErrorCancelled = 1223;

    public bool WaitForParentExit(int processId, long startTimeUtcTicks, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.StartTime.ToUniversalTime().Ticks != startTimeUtcTicks)
            {
                return false;
            }

            return process.WaitForExit(timeout);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    public int RunInstaller(string installerPath)
    {
        using var process = Process.Start(CreateInstallerStartInfo(installerPath)) ??
            throw new InvalidOperationException("Windows Installer did not start.");
        process.WaitForExit();
        return process.ExitCode;
    }

    internal static ProcessStartInfo CreateInstallerStartInfo(string installerPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            UseShellExecute = true,
            Verb = "runas"
        };
        startInfo.ArgumentList.Add("/i");
        startInfo.ArgumentList.Add(installerPath);
        startInfo.ArgumentList.Add("/passive");
        startInfo.ArgumentList.Add("/norestart");
        return startInfo;
    }

    public bool StartApplication(string applicationPath)
    {
        try
        {
            return File.Exists(applicationPath) && Process.Start(new ProcessStartInfo
            {
                FileName = applicationPath,
                WorkingDirectory = Path.GetDirectoryName(applicationPath),
                UseShellExecute = true
            }) is not null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    public void ShowFatalError(string message) => MessageBox(0, message, "GhostSlacking 更新失败", 0x10);

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint window, string text, string caption, uint type);
}
