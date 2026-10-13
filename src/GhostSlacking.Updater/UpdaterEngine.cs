using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.Updater;

internal sealed record UpdaterOptions(
    int ParentProcessId,
    long ParentStartTimeUtcTicks,
    string InstallerPath,
    string Version,
    string ApplicationPath,
    string ReadyEventName)
{
    public string ManifestPath => Path.Combine(Path.GetDirectoryName(InstallerPath)!, "release.json");
    public string SignaturePath => ManifestPath + ".sig";
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
            var expectedInstallerName = SignedReleaseManifest.InstallerName(normalizedVersion);
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
        if (!ReleaseVersion.TryParse(value, out var version))
        {
            return false;
        }

        normalized = version.Text;
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
    bool PrepareExit(int processId, long startTimeUtcTicks, string applicationPath);
    int RunInstaller(string installerPath, string installRoot);
    bool StartApplication(string applicationPath, string? expectedVersion, Action confirmed);
    void ShowFatalError(string message);
}

internal sealed class UpdaterEngine(
    IUpdaterRuntime runtime,
    UpdateCompletionStore completionStore,
    ILogger logger,
    string? releasePublicKey = null)
{
    public int Run(UpdaterOptions options, Func<bool>? authorize = null)
    {
        FileStream verified;
        var phaseStartedAt = Stopwatch.GetTimestamp();
        try
        {
            var manifest = SignedReleaseManifest.VerifyFiles(options.ManifestPath, options.SignaturePath, releasePublicKey);
            if (manifest.Version != options.Version) throw new InvalidDataException("The handoff version does not match the signed release.");
            verified = manifest.OpenVerifiedInstaller(options.InstallerPath);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            logger.Log(LogLevel.Error, "Cached update verification failed before application shutdown.", exception);
            completionStore.Save(Result(options, UpdateCompletionStatus.Failed, null, "verification"));
            return 2;
        }
        finally { LogElapsed("verification", phaseStartedAt); }
        using (verified)
        {
            if (authorize is not null && !authorize())
            {
                completionStore.Save(Result(options, UpdateCompletionStatus.Cancelled, null, "handoff"));
                return 9;
            }
            logger.Log(LogLevel.Info, "UpdatePhase preparing: waiting for safe application and watchdog shutdown.");
            phaseStartedAt = Stopwatch.GetTimestamp();
            bool prepared;
            try { prepared = runtime.PrepareExit(options.ParentProcessId, options.ParentStartTimeUtcTicks, options.ApplicationPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
            {
                logger.Log(LogLevel.Error, "Safe shutdown could not be requested.", exception);
                prepared = false;
            }
            finally { LogElapsed("shutdown", phaseStartedAt); }
            if (!prepared)
            {
                completionStore.Save(Result(options, UpdateCompletionStatus.Failed, null, "shutdown"));
                runtime.ShowFatalError("GhostSlacking 未能安全退出或恢复窗口。程序文件尚未更改，请检查日志后重试。");
                return 4;
            }
            int installerExitCode;
            phaseStartedAt = Stopwatch.GetTimestamp();
            try
            {
                logger.Log(LogLevel.Info, "UpdatePhase installing.");
                installerExitCode = runtime.RunInstaller(options.InstallerPath,
                    Path.GetDirectoryName(Path.GetDirectoryName(options.ApplicationPath))!);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                logger.Log(LogLevel.Error, "The NSIS installer could not be started.", exception);
                return Finish(options, UpdateCompletionStatus.Failed, null, 6);
            }
            finally { LogElapsed("installation", phaseStartedAt); }
            return installerExitCode switch
            {
                0 => Finish(options, UpdateCompletionStatus.Succeeded, 0, 0),
                1 => Finish(options, UpdateCompletionStatus.Cancelled, 1, 5),
                _ => Finish(options, UpdateCompletionStatus.Failed, installerExitCode, 7)
            };
        }
    }

    private int Finish(
        UpdaterOptions options,
        UpdateCompletionStatus status,
        int? installerExitCode,
        int updaterExitCode)
    {
        if (status != UpdateCompletionStatus.Succeeded)
            completionStore.Save(Result(options, status, installerExitCode, "installation"));
        logger.Log(LogLevel.Info, "UpdatePhase starting: confirming the installed application version.");
        var phaseStartedAt = Stopwatch.GetTimestamp();
        var started = runtime.StartApplication(options.ApplicationPath, status == UpdateCompletionStatus.Succeeded ? options.Version : null,
            () => completionStore.Save(Result(options, status, installerExitCode)));
        LogElapsed("startup", phaseStartedAt);
        if (started)
        {
            return updaterExitCode;
        }

        logger.Log(LogLevel.Error, "The application could not be restarted after the update attempt.");
        completionStore.Save(Result(options, UpdateCompletionStatus.LaunchFailed, installerExitCode, "startup"));
        runtime.ShowFatalError(
            $"GhostSlacking 无法在更新后自动启动。请手动启动应用并查看日志：{Path.Combine(GhostSlackingDataPaths.LogDirectory, "updater.log")}");
        return 8;
    }

    private void LogElapsed(string phase, long startedAt) => logger.Log(LogLevel.Info,
        $"UpdatePhase {phase} elapsedMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F0}");

    private static UpdateCompletionResult Result(
        UpdaterOptions options,
        UpdateCompletionStatus status,
        int? installerExitCode,
        string? failureStage = null) => new(
        options.Version,
        status,
        installerExitCode,
        DateTimeOffset.UtcNow,
        failureStage);
}

internal sealed class WindowsUpdaterRuntime : IUpdaterRuntime
{
    internal const int Success = 0;

    public bool PrepareExit(int processId, long startTimeUtcTicks, string applicationPath)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.StartTime.ToUniversalTime().Ticks != startTimeUtcTicks ||
                !string.Equals(process.MainModule?.FileName, applicationPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !UserInstallation.IsElevated && UserInstallation.CloseSafely(Path.GetDirectoryName(applicationPath)!);
        }
        catch (ArgumentException)
        {
            return UserInstallation.CloseSafely(Path.GetDirectoryName(applicationPath)!);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    public int RunInstaller(string installerPath, string installRoot)
    {
        using var process = Process.Start(CreateInstallerStartInfo(installerPath, installRoot)) ??
            throw new InvalidOperationException("The NSIS installer did not start.");
        process.WaitForExit();
        return process.ExitCode;
    }

    internal static ProcessStartInfo CreateInstallerStartInfo(string installerPath, string? installRoot = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("/PASSIVE");
        startInfo.ArgumentList.Add("/UPDATE");
        // NSIS restores the registered root. Its special /D syntax deliberately does not
        // follow standard Windows argv quoting, so do not pass a second directory source.
        return startInfo;
    }

    public bool StartApplication(string applicationPath, string? expectedVersion, Action confirmed)
    {
        try
        {
            if (expectedVersion is not null) return UpdateStartupHandshake.StartAndWait(applicationPath, expectedVersion, confirmed);
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
