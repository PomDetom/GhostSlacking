using System.ComponentModel;
using GhostSlacking.Core;
using GhostSlacking.Updater;

namespace GhostSlacking.App.Tests;

public sealed class UpdaterEngineTests
{
    [Fact]
    public void Valid_options_require_the_expected_cache_and_registered_application_paths()
    {
        using var files = new UpdaterTestFiles();

        var parsed = UpdaterOptions.TryParse(
            files.Arguments,
            files.UpdatesRoot,
            files.InstallLocation,
            out var options,
            out var error);

        Assert.True(parsed, error);
        Assert.Equal(files.InstallerPath, options.InstallerPath);
        Assert.Equal(files.ApplicationPath, options.ApplicationPath);
        Assert.Equal("1.2.0", options.Version);
        Assert.Equal(files.ReadyEventName, options.ReadyEventName);
    }

    [Fact]
    public void Options_reject_an_installer_outside_the_update_cache()
    {
        using var files = new UpdaterTestFiles();
        var outsideInstaller = Path.Combine(files.Root, "GhostSlacking-1.2.0-win-x64.msi");
        File.WriteAllText(outsideInstaller, "test");
        var args = files.Arguments.ToArray();
        args[Array.IndexOf(args, "--installer") + 1] = outsideInstaller;

        var parsed = UpdaterOptions.TryParse(
            args,
            files.UpdatesRoot,
            files.InstallLocation,
            out _,
            out _);

        Assert.False(parsed);
    }

    [Fact]
    public void Options_reject_an_application_outside_the_registered_install_location()
    {
        using var files = new UpdaterTestFiles();
        var outsideApplication = Path.Combine(files.Root, "GhostSlacking.App.exe");
        File.WriteAllText(outsideApplication, "test");
        var args = files.Arguments.ToArray();
        args[Array.IndexOf(args, "--application") + 1] = outsideApplication;

        var parsed = UpdaterOptions.TryParse(
            args,
            files.UpdatesRoot,
            files.InstallLocation,
            out _,
            out _);

        Assert.False(parsed);
    }

    [Fact]
    public void Options_reject_an_untrusted_readiness_event_name()
    {
        using var files = new UpdaterTestFiles();
        var args = files.Arguments.ToArray();
        args[Array.IndexOf(args, "--ready-event") + 1] = @"Global\ArbitraryEvent";

        var parsed = UpdaterOptions.TryParse(
            args,
            files.UpdatesRoot,
            files.InstallLocation,
            out _,
            out _);

        Assert.False(parsed);
    }

    [Fact]
    public void Windows_installer_uses_elevation_and_passive_non_restarting_arguments()
    {
        var startInfo = WindowsUpdaterRuntime.CreateInstallerStartInfo(@"C:\updates\GhostSlacking-1.2.0-win-x64.msi");

        Assert.Equal("msiexec.exe", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal("runas", startInfo.Verb);
        Assert.Equal(
            ["/i", @"C:\updates\GhostSlacking-1.2.0-win-x64.msi", "/passive", "/norestart"],
            startInfo.ArgumentList);
    }

    [Theory]
    [InlineData(WindowsUpdaterRuntime.Success)]
    [InlineData(WindowsUpdaterRuntime.SuccessRebootInitiated)]
    [InlineData(WindowsUpdaterRuntime.SuccessRebootRequired)]
    public void Successful_installer_codes_write_success_and_restart_the_application(int installerExitCode)
    {
        using var files = new UpdaterTestFiles();
        var runtime = new RecordingUpdaterRuntime { InstallerExitCode = installerExitCode };
        var store = new UpdateCompletionStore(files.ResultPath);
        var exitCode = new UpdaterEngine(runtime, store, NullLogger.Instance).Run(files.Options);

        Assert.Equal(0, exitCode);
        Assert.True(runtime.ApplicationStarted);
        var result = store.Consume();
        Assert.NotNull(result);
        Assert.Equal(UpdateCompletionStatus.Succeeded, result.Status);
        Assert.Equal(installerExitCode, result.InstallerExitCode);
        Assert.Null(store.Consume());
    }

    [Fact]
    public void Cancelled_elevation_writes_cancelled_result_and_restores_the_application()
    {
        using var files = new UpdaterTestFiles();
        var runtime = new RecordingUpdaterRuntime
        {
            InstallerException = new Win32Exception(WindowsUpdaterRuntime.ErrorCancelled)
        };
        var store = new UpdateCompletionStore(files.ResultPath);

        var exitCode = new UpdaterEngine(runtime, store, NullLogger.Instance).Run(files.Options);

        Assert.Equal(5, exitCode);
        Assert.True(runtime.ApplicationStarted);
        Assert.Equal(UpdateCompletionStatus.Cancelled, store.Consume()?.Status);
    }

    [Fact]
    public void Failed_installer_writes_failure_and_restores_the_application()
    {
        using var files = new UpdaterTestFiles();
        var runtime = new RecordingUpdaterRuntime { InstallerExitCode = 1603 };
        var store = new UpdateCompletionStore(files.ResultPath);

        var exitCode = new UpdaterEngine(runtime, store, NullLogger.Instance).Run(files.Options);

        Assert.Equal(7, exitCode);
        Assert.True(runtime.ApplicationStarted);
        var result = store.Consume();
        Assert.NotNull(result);
        Assert.Equal(UpdateCompletionStatus.Failed, result.Status);
        Assert.Equal(1603, result.InstallerExitCode);
    }

    [Fact]
    public void Parent_exit_timeout_does_not_start_installer_or_a_second_application()
    {
        using var files = new UpdaterTestFiles();
        var runtime = new RecordingUpdaterRuntime { ParentExited = false };
        var store = new UpdateCompletionStore(files.ResultPath);

        var exitCode = new UpdaterEngine(runtime, store, NullLogger.Instance).Run(files.Options);

        Assert.Equal(4, exitCode);
        Assert.False(runtime.InstallerStarted);
        Assert.False(runtime.ApplicationStarted);
        Assert.Equal(UpdateCompletionStatus.Failed, store.Consume()?.Status);
    }

    [Fact]
    public void Application_restart_failure_is_recorded_and_shows_a_fallback_error()
    {
        using var files = new UpdaterTestFiles();
        var runtime = new RecordingUpdaterRuntime { ApplicationStartResult = false };
        var store = new UpdateCompletionStore(files.ResultPath);

        var exitCode = new UpdaterEngine(runtime, store, NullLogger.Instance).Run(files.Options);

        Assert.Equal(8, exitCode);
        Assert.True(runtime.FatalErrorShown);
        Assert.Equal(UpdateCompletionStatus.Failed, store.Consume()?.Status);
    }

    [Fact]
    public void Corrupt_completion_result_is_deleted_after_one_read_attempt()
    {
        using var files = new UpdaterTestFiles();
        File.WriteAllText(files.ResultPath, "not json");
        var store = new UpdateCompletionStore(files.ResultPath);

        Assert.Null(store.Consume());
        Assert.False(File.Exists(files.ResultPath));
        Assert.Null(store.Consume());
    }

    private sealed class RecordingUpdaterRuntime : IUpdaterRuntime
    {
        public bool ParentExited { get; init; } = true;
        public int InstallerExitCode { get; init; }
        public Exception? InstallerException { get; init; }
        public bool ApplicationStartResult { get; init; } = true;
        public bool InstallerStarted { get; private set; }
        public bool ApplicationStarted { get; private set; }
        public bool FatalErrorShown { get; private set; }

        public bool WaitForParentExit(int processId, long startTimeUtcTicks, TimeSpan timeout) => ParentExited;

        public int RunInstaller(string installerPath)
        {
            InstallerStarted = true;
            if (InstallerException is not null)
            {
                throw InstallerException;
            }

            return InstallerExitCode;
        }

        public bool StartApplication(string applicationPath)
        {
            ApplicationStarted = true;
            return ApplicationStartResult;
        }

        public void ShowFatalError(string message) => FatalErrorShown = true;
    }

    private sealed class UpdaterTestFiles : IDisposable
    {
        public UpdaterTestFiles()
        {
            Root = Path.Combine(Path.GetTempPath(), $"GhostSlacking.Updater.Tests.{Guid.NewGuid():N}");
            UpdatesRoot = Path.Combine(Root, "updates");
            InstallLocation = Path.Combine(Root, "installed");
            InstallerPath = Path.Combine(UpdatesRoot, "1.2.0", "GhostSlacking-1.2.0-win-x64.msi");
            ApplicationPath = Path.Combine(InstallLocation, "GhostSlacking.App.exe");
            ReadyEventName = $@"Local\GhostSlacking.Updater.Ready.{Guid.NewGuid():N}";
            ResultPath = Path.Combine(Root, "update-completion.json");
            Directory.CreateDirectory(Path.GetDirectoryName(InstallerPath)!);
            Directory.CreateDirectory(InstallLocation);
            File.WriteAllText(InstallerPath, "test installer");
            File.WriteAllText(ApplicationPath, "test application");
            Options = new UpdaterOptions(123, 456, InstallerPath, "1.2.0", ApplicationPath, ReadyEventName);
            Arguments =
            [
                "--parent-pid", "123",
                "--parent-start-ticks", "456",
                "--installer", InstallerPath,
                "--version", "1.2.0",
                "--application", ApplicationPath,
                "--ready-event", ReadyEventName
            ];
        }

        public string Root { get; }
        public string UpdatesRoot { get; }
        public string InstallLocation { get; }
        public string InstallerPath { get; }
        public string ApplicationPath { get; }
        public string ReadyEventName { get; }
        public string ResultPath { get; }
        public UpdaterOptions Options { get; }
        public string[] Arguments { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
