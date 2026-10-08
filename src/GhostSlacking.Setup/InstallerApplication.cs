using System.Diagnostics;
using System.IO;
using System.Windows;
using GhostSlacking.Core;
using GhostSlacking.Platform;
using Microsoft.Win32;
using WixToolset.BootstrapperApplicationApi;
using BurnLogLevel = WixToolset.BootstrapperApplicationApi.LogLevel;

namespace GhostSlacking.Setup;

internal sealed class InstallerApplication : BootstrapperApplication
{
    private IBootstrapperCommand _command = null!;
    private InstallerWindow _window = null!;
    private readonly InstallerState _state = new();
    private readonly Stopwatch _applyClock = new();
    private readonly Stopwatch _packageClock = new();
    private readonly Stopwatch _elevatedClock = new();
    private readonly RollingFileLogger _logger = new(Path.Combine(GhostSlackingDataPaths.LogDirectory, "installer.log"));
    private string? _installedDirectory;
    private string _version = "";
    private bool _present;
    private bool _downgrade;
    private volatile bool _cancel;
    private volatile bool _rollingBack;
    private volatile InstallerSelection? _selection;
    private string? _installerError;
    private int _exitCode = 1602;
    private LaunchAction _action;
    private string? _lastAction;
    private long _lastActionTimestamp;
    private bool Automatic => _command.Display != Display.Full;

    protected override void OnCreate(CreateEventArgs args)
    {
        base.OnCreate(args);
        _command = args.Command;
    }

    protected override void Run()
    {
        try
        {
            _version = engine.GetVariableString("DisplayVersion");
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(@"Software\GhostSlacking");
            _installedDirectory = key?.GetValue("InstallLocation") as string;
            if (string.IsNullOrWhiteSpace(_installedDirectory)) _installedDirectory = null;
            if (!string.IsNullOrWhiteSpace(_installedDirectory))
            {
                engine.SetVariableString("InstallFolder", _installedDirectory, false);
                var oldVersion = key?.GetValue("DisplayVersion") as string;
                var appPath = Path.Combine(_installedDirectory, "GhostSlacking.App.exe");
                if (oldVersion is null && File.Exists(appPath)) oldVersion = FileVersionInfo.GetVersionInfo(appPath).ProductVersion?.Split('+')[0];
                _downgrade = ReleaseVersion.TryParse(oldVersion, out var old) && old > ReleaseVersion.Parse(_version);
            }
            _window = new InstallerWindow(_installedDirectory ?? engine.FormatString(engine.GetVariableString("InstallFolder")))
            {
                Title = $"GhostSlacking {_version} 安装"
            };
            _window.StartRequested += async (_, _) => await BeginAsync();
            _window.FinishRequested += (_, _) =>
            {
                if (_state.Phase != InstallerPhase.Succeeded) return;
                if (_action != LaunchAction.Uninstall && _selection?.LaunchAfterInstall == true) LaunchApplication();
                _window.Close();
            };
            _window.CancelRequested += (_, _) => { if (_state.IsBusy) _cancel = true; else _window.Close(); };
            _window.Closing += (_, args) => { if (_state.IsBusy) { _cancel = true; args.Cancel = true; } };
            _window.Loaded += (_, _) =>
            {
                if (_command.Display == Display.None) _window.Hide();
                engine.CloseSplashScreen(); engine.Detect(_window.Handle);
            };
            new Application().Run(_window);
            _logger.Dispose();
            engine.Quit(_exitCode);
        }
        catch (Exception exception)
        {
            Log(BurnLogLevel.Error, exception.ToString());
            _logger.Dispose();
            engine.Quit(1603);
        }
    }

    protected override void OnDetectPackageComplete(DetectPackageCompleteEventArgs args)
    {
        if (args.PackageId == "GhostSlackingMsi") _present = args.State == PackageState.Present;
        base.OnDetectPackageComplete(args);
    }

    protected override void OnDetectRelatedBundle(DetectRelatedBundleEventArgs args)
    {
        if (args.RelationType == RelationType.Upgrade && engine.CompareVersions(args.Version, engine.GetVariableVersion("WixBundleVersion")) > 0)
            _downgrade = true;
        base.OnDetectRelatedBundle(args);
    }

    protected override void OnDetectComplete(DetectCompleteEventArgs args)
    {
        Ui(async () =>
        {
            if (_command.Action is LaunchAction.Layout or LaunchAction.UpdateReplace or LaunchAction.UpdateReplaceEmbedded)
            {
                Fail("不支持此安装模式。", 1639); return;
            }
            _action = _command.Action == LaunchAction.Uninstall ? LaunchAction.Uninstall
                : _present ? LaunchAction.Repair : LaunchAction.Install;
            if (args.Status < 0) { Fail("无法读取安装状态。", args.Status); return; }
            if (_downgrade && _action != LaunchAction.Uninstall) { Fail("已安装更高版本，无法降级。", 1638); return; }
            var action = _action == LaunchAction.Uninstall ? "卸载" : _action == LaunchAction.Repair ? "修复" : _installedDirectory is null ? "安装" : "更新";
            _window.ConfigureReady(action, _installedDirectory is null && _action == LaunchAction.Install,
                manual: !Automatic, uninstall: _action == LaunchAction.Uninstall);
            if (Automatic) await BeginAsync();
        });
        base.OnDetectComplete(args);
    }

    private async Task BeginAsync()
    {
        if (_state.Phase != InstallerPhase.Ready) return;
        try
        {
            var selection = _window.ReadSelection();
            _selection = _installedDirectory is null && _action == LaunchAction.Install
                ? selection.ValidateDirectory() : selection;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            if (Automatic) Fail(exception.Message, 1603);
            else _window.ShowFailure(exception.Message);
            return;
        }
        _window.BeginInstall();
        _state.Begin();
        _window.SetProgress(0, "正在安全退出应用并恢复窗口…");
        var closeClock = Stopwatch.StartNew();
        try
        {
            if (_action == LaunchAction.Install && !HasRuntime())
            {
                Fail("需要 .NET 8 Runtime x64，请安装后重试。", 1603, offerRuntimeDownload: true); return;
            }
            // Same deadline as the updater; no forced termination and no fixed delay after exit.
            await Task.Run(() => InstallerShutdown.CloseAndWait(_installedDirectory, TimeSpan.FromSeconds(30)));
            Log(BurnLogLevel.Standard, $"Timing safe_close_ms={closeClock.Elapsed.TotalMilliseconds:F1}");
            if (_cancel) { Fail("已取消，旧版本未更改。", 1602); return; }
            if (_installedDirectory is null)
            {
                engine.SetVariableString("InstallFolder", _selection!.Directory, false);
            }
            _state.Install();
            _window.SetProgress(0, "正在准备安装…");
            engine.Plan(_action);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        { Fail(exception.Message, 1603); }
    }

    protected override void OnPlanMsiFeature(PlanMsiFeatureEventArgs args)
    {
        // Preserve MSI feature migration for upgrades and recommended states for repair/removal.
        if (_installedDirectory is null && _action == LaunchAction.Install)
        {
            args.State = args.FeatureId switch
            {
                "MainProgramFeature" => FeatureState.Local,
                "StartMenuShortcutFeature" => _selection!.StartMenuShortcut ? FeatureState.Local : FeatureState.Absent,
                "DesktopShortcutFeature" => _selection!.DesktopShortcut ? FeatureState.Local : FeatureState.Absent,
                _ => args.State
            };
        }
        base.OnPlanMsiFeature(args);
    }

    protected override void OnPlanComplete(PlanCompleteEventArgs args)
    {
        Ui(() =>
        {
            if (args.Status < 0) Fail("无法准备安装。", args.Status);
            else if (_cancel) Fail("已取消，旧版本未更改。", 1602);
            else { _applyClock.Restart(); engine.Apply(_window.Handle); }
        });
        base.OnPlanComplete(args);
    }

    protected override void OnProgress(ProgressEventArgs args)
    {
        args.Cancel |= _cancel && !_rollingBack;
        Ui(() =>
        {
            if (_state.Phase is not (InstallerPhase.Installing or InstallerPhase.RollingBack)) return;
            _state.Progress(args.OverallPercentage);
            _window.SetProgress(_state.Percent, _state.Phase == InstallerPhase.RollingBack ? "正在回滚…"
                : $"正在{(_action == LaunchAction.Uninstall ? "卸载" : "安装")} · {_state.Percent}%");
        });
        base.OnProgress(args);
    }

    protected override void OnExecutePackageBegin(ExecutePackageBeginEventArgs args)
    {
        if (args.PackageId == "GhostSlackingMsi") _packageClock.Restart();
        if (!args.ShouldExecute)
        {
            _rollingBack = true;
            Ui(() => { _state.Rollback(); _window.SetProgress(0, "正在回滚，恢复原版本…"); });
        }
        else args.Cancel |= _cancel;
        base.OnExecutePackageBegin(args);
    }

    protected override void OnExecuteMsiMessage(ExecuteMsiMessageEventArgs args)
    {
        if (args.MessageType == InstallMessage.ActionStart)
        {
            LogLastAction();
            _lastAction = args.Data.Count > 0 ? args.Data[0] : args.Message;
            _lastActionTimestamp = Stopwatch.GetTimestamp();
        }
        base.OnExecuteMsiMessage(args);
    }

    protected override void OnApplyComplete(ApplyCompleteEventArgs args)
    {
        var code = args.Status < 0 ? args.Status & 0xFFFF : args.Restart != ApplyRestart.None ? 3010 : 0;
        LogLastAction();
        Log(BurnLogLevel.Standard, $"Timing apply_ms={_applyClock.Elapsed.TotalMilliseconds:F1} package_ms={_packageClock.Elapsed.TotalMilliseconds:F1} post_uac_ms={(_elevatedClock.IsRunning ? _elevatedClock.Elapsed.TotalMilliseconds : 0):F1} exit_code={code}");
        Ui(() =>
        {
            _exitCode = code;
            _state.Complete(code);
            var succeeded = _state.Phase == InstallerPhase.Succeeded;
            var message = succeeded ? (_action == LaunchAction.Uninstall ? "卸载完成" : "安装成功，点击图标完成")
                : _state.Phase == InstallerPhase.Cancelled ? "已取消，安装已停止"
                : $"安装未完成（错误 {code}）。\n{_installerError}\n日志：{GhostSlackingDataPaths.LogDirectory}";
            _window.SetProgress(_state.Percent, message);
            if (!Automatic) _window.EnableCompletion(succeeded);
            // Errors are shown only after ApplyComplete, including the rollback attempt.
            if (_state.Phase == InstallerPhase.Failed && _command.Display != Display.None) _window.ShowFailure(message);
            // Render the completed icon before closing; no fixed wait and no app launch here.
            if (Automatic || _state.Phase == InstallerPhase.Cancelled) _window.CloseAfterRender();
        });
        base.OnApplyComplete(args);
    }

    protected override void OnError(WixToolset.BootstrapperApplicationApi.ErrorEventArgs args)
    {
        _installerError = args.ErrorMessage;
        base.OnError(args);
    }

    private void Fail(string message, int code, bool offerRuntimeDownload = false)
    {
        _exitCode = code < 0 ? code & 0xFFFF : code;
        _state.Complete(_exitCode);
        _window.SetProgress(0, message);
        if (!Automatic) _window.EnableCompletion(succeeded: false);
        Log(BurnLogLevel.Error, message);
        if (_state.Phase == InstallerPhase.Failed && _command.Display != Display.None &&
            _window.ShowFailure(message, offerRuntimeDownload) && offerRuntimeDownload)
        {
            try { Process.Start(new ProcessStartInfo("https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0") { UseShellExecute = true }); }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            { Log(BurnLogLevel.Error, $"Runtime download page could not be opened: {exception.Message}"); }
        }
        if (Automatic || _state.Phase == InstallerPhase.Cancelled) _window.CloseAfterRender();
    }

    private void LaunchApplication()
    {
        try
        {
            var path = Path.Combine(engine.GetVariableString("InstallFolder"), "GhostSlacking.App.exe");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log(BurnLogLevel.Error, $"Application launch failed: {exception.Message}");
            _window.ShowFailure("安装已完成，但应用未能启动。请从开始菜单或安装目录启动应用。");
        }
    }

    private static bool HasRuntime()
    {
        // .NET registers x64 runtime inventory in the 32-bit registry view too.
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var key = root.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App");
        return key?.GetValueNames().Any(name => Version.TryParse(name, out var version) && version.Major == 8) == true;
    }

    private void Ui(Action action) => _window.Dispatcher.BeginInvoke(action);

    protected override void OnElevateComplete(ElevateCompleteEventArgs args)
    {
        if (args.Status >= 0) _elevatedClock.Restart();
        base.OnElevateComplete(args);
    }

    private void LogLastAction()
    {
        if (_lastAction is not null && (_lastAction is "InstallFiles" or "RemoveExistingProducts" or "InstallFinalize" ||
            _command.CommandLine.Contains("--diagnostics", StringComparison.Ordinal)))
            // Nested MSI installations also emit ActionStart; this is an event
            // interval. Use the native server-thread log for action duration.
            Log(BurnLogLevel.Standard, $"Timing msi_action={_lastAction} next_action_event_ms={Stopwatch.GetElapsedTime(_lastActionTimestamp).TotalMilliseconds:F1}");
        _lastAction = null;
    }

    private void Log(BurnLogLevel level, string message)
    {
        engine.Log(level, message);
        _logger.Log(level == BurnLogLevel.Error ? GhostSlacking.Core.LogLevel.Error : GhostSlacking.Core.LogLevel.Info, message);
    }
}
