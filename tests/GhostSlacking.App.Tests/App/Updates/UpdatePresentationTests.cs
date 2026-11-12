using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using GhostSlacking.App;
using GhostSlacking.Core;
using Button = Avalonia.Controls.Button;
using ComboBox = Avalonia.Controls.ComboBox;

namespace GhostSlacking.App.Tests;

// Shares the application's single STA test fixture. Managers, settings and installers are all isolated.
internal static class UpdatePresentationTests
{
    internal static void VerifyUi()
    {
        About_action_tracks_update_state_without_saving_drafts();
        Update_menu_preserves_skip_resume_recheck_and_release_actions();
        Download_can_pause_stop_and_continue_in_background();
        Closing_the_window_pauses_without_installing();
        Verification_can_pause_but_install_handoff_disables_controls();
        Portable_and_current_copies_offer_only_valid_actions();
        Localized_windows_fit_and_release_notes_scroll();
    }

    private static void About_action_tracks_update_state_without_saving_drafts()
    {
        var localDate = new DateTime(2026, 9, 10, 16, 30, 0, DateTimeKind.Unspecified);
        var checkedAt = new DateTimeOffset(localDate, TimeZoneInfo.Local.GetUtcOffset(localDate));
        using var updates = new TestUpdateManager(new(ApplicationUpdateStatus.Available, "1.1.0", Release(), LastSuccessfulCheckUtc: checkedAt));
        var opened = 0;
        var saves = 0;
        var initial = new AppSettings();
        var window = new SettingsWindow(initial, _ => { saves++; return true; },
            updates: updates, openUpdateWindow: () => opened++, initialPage: "about");
        try
        {
            var action = Field<Button>(window, "_checkUpdatesButton");
            var checkTime = Field<TextBlock>(window, "_lastUpdateCheckText");
            var status = Field<TextBlock>(window, "_updateStatusText");
            var about = Field<NavigationViewItem>(window, "_aboutItem");
            Assert.Contains(about, Field<NavigationView>(window, "_navigation").FooterMenuItems.Cast<object>());
            Assert.Equal("关于", about.Content);
            Assert.Equal("关于", Field<TextBlock>(window, "_pageTitle").Text);
            var source = Field<Button>(window, "_sourceRepositoryButton");
            var releases = Field<Button>(window, "_releasePageButton");
            Assert.Equal("GitHub 项目", source.Content);
            Assert.Equal("发布页", releases.Content);
            var links = Assert.IsType<StackPanel>(source.Parent);
            Assert.Same(links, releases.Parent);
            Assert.Same(source, links.Children[0]);
            Assert.Same(releases, links.Children[1]);
            Assert.Equal("当前版本：1.1.0", Field<TextBlock>(window, "_currentVersionText").Text);
            Assert.Equal("最新版本：1.2.0", Field<TextBlock>(window, "_latestVersionText").Text);
            Assert.Equal("上次成功检查：2026-09-10 16:30", checkTime.Text);
            Assert.Same(checkTime.Parent, status.Parent);
            Assert.Same(checkTime.Parent!.Parent, action.Parent);
            Assert.Equal("查看更新", action.Content);
            Click(action);
            Assert.Equal(1, opened);
            Assert.Equal(0, updates.CheckCalls);
            Assert.Equal(SettingsStatus.None, Field<SettingsEditState>(window, "_editState").Status);
            window.ApplyAdjustmentResult(initial with { RevealDiameterPx = initial.RevealDiameterPx + 8 }, AdjustmentKind.Diameter);

            foreach (var state in new[] { ApplicationUpdateStatus.Skipped, ApplicationUpdateStatus.Error, ApplicationUpdateStatus.Downloading, ApplicationUpdateStatus.Verifying, ApplicationUpdateStatus.Ready, ApplicationUpdateStatus.Paused })
            {
                updates.SetStatus(state, 42);
                Assert.True(action.IsEnabled);
                Assert.Equal(UiText.Text(UiLanguage.Chinese,
                    state is ApplicationUpdateStatus.Downloading or ApplicationUpdateStatus.Verifying or ApplicationUpdateStatus.Paused ? "viewUpdateProgress" : "viewUpdateDetails"), action.Content);
                Click(action);
            }
            Assert.Equal(7, opened);
            Assert.Equal("上次成功检查：2026-09-10 16:30", checkTime.Text);
            foreach (var state in new[] { ApplicationUpdateStatus.Checking, ApplicationUpdateStatus.Preparing, ApplicationUpdateStatus.Pausing, ApplicationUpdateStatus.Stopping })
            {
                updates.SetStatus(state);
                Assert.False(action.IsEnabled);
                Click(action);
            }
            Assert.Equal(7, opened);
            Assert.Equal(SettingsStatus.Modified, Field<SettingsEditState>(window, "_editState").Status);
            Assert.Equal(initial.Normalize(), Field<SettingsEditState>(window, "_editState").SavedSettings);
            Assert.Equal(initial.RevealDiameterPx + 8, Field<NumberBox>(window, "_diameter").Value);
            Assert.Equal(0, saves);
        }
        finally { window.Close(); }

        using var uncheckedUpdates = new TestUpdateManager(new(ApplicationUpdateStatus.Idle, "1.1.0"));
        var uncheckedWindow = new SettingsWindow(new AppSettings { Language = UiLanguage.English }, _ => true,
            updates: uncheckedUpdates, initialPage: "about");
        try
        {
            var action = Field<Button>(uncheckedWindow, "_checkUpdatesButton");
            Assert.Equal("No successful update check yet", Field<TextBlock>(uncheckedWindow, "_lastUpdateCheckText").Text);
            foreach (var state in new[] { ApplicationUpdateStatus.Idle, ApplicationUpdateStatus.UpToDate, ApplicationUpdateStatus.NoCompatibleRelease, ApplicationUpdateStatus.Error })
            {
                uncheckedUpdates.SetStatus(state);
                Assert.Equal("Check for updates", action.Content);
                Click(action);
                uncheckedUpdates.CompleteCheck();
            }
            Assert.Equal(4, uncheckedUpdates.CheckCalls);
        }
        finally { uncheckedWindow.Close(); }
    }

    private static void Update_menu_preserves_skip_resume_recheck_and_release_actions()
    {
        using var updates = AvailableUpdates();
        var window = new UpdateWindow(updates, UiLanguage.Chinese);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        var more = Field<Button>(window, "_moreButton");
        Assert.Equal("版本 1.2.0", Field<TextBlock>(window, "_versionText").Text);
        Assert.Equal("当前版本：1.1.0  ·  安装包 12.0 MB", Field<TextBlock>(window, "_packageText").Text);
        var menu = Assert.IsType<MenuFlyout>(more.Flyout);
        Assert.Contains(Field<MenuItem>(window, "_releaseItem"), menu.Items);
        Assert.Contains(Field<MenuItem>(window, "_skipItem"), menu.Items);
        Assert.Contains(Field<MenuItem>(window, "_recheckItem"), menu.Items);
        Click(Field<MenuItem>(window, "_releaseItem"));
        Assert.Equal(1, updates.ReleasePageCalls);
        Click(Field<MenuItem>(window, "_recheckItem"));
        Assert.Equal(1, updates.CheckCalls);
        Assert.False(Field<MenuItem>(window, "_recheckItem").IsEnabled);
        Assert.False(Field<MenuItem>(window, "_skipItem").IsEnabled);
        Assert.False(Field<Button>(window, "_installButton").IsEnabled);
        updates.CompleteCheck();
        Click(Field<MenuItem>(window, "_skipItem"));
        Assert.True(closed);
        Assert.Equal(ApplicationUpdateStatus.Skipped, updates.Snapshot.Status);

        var resumedWindow = new UpdateWindow(updates, UiLanguage.Chinese);
        try
        {
            var resume = Field<MenuItem>(resumedWindow, "_skipItem");
            Assert.Equal("恢复提醒", resume.Header);
            Click(resume);
            Assert.Equal(ApplicationUpdateStatus.Available, updates.Snapshot.Status);
            Assert.False(Field<MenuItem>(resumedWindow, "_skipItem").Header?.Equals("恢复提醒"));
            resumedWindow.RefreshLanguage(UiLanguage.English);
            Assert.Equal("Skip this version", resume.Header);
            Assert.Equal("More", Field<Button>(resumedWindow, "_moreButton").Content);
            Assert.Contains(Field<StackPanel>(resumedWindow, "_notesPanel").Children.OfType<TextBlock>(), text => text.Text == "• Show what's new.");
            Assert.True(Field<Border>(resumedWindow, "_historyWarning").IsVisible);
        }
        finally { resumedWindow.Close(); }
    }

    private static void Download_can_pause_stop_and_continue_in_background()
    {
        using var updates = AvailableUpdates();
        var window = new UpdateWindow(updates, UiLanguage.Chinese);
        var install = Field<Button>(window, "_installButton");
        Click(install);
        Assert.Equal(1, updates.DownloadCalls);
        Assert.Equal("暂停", install.Content);
        Assert.True(Field<Button>(window, "_backgroundButton").IsVisible);
        Assert.False(Field<Button>(window, "_laterButton").IsVisible);
        updates.SetStatus(ApplicationUpdateStatus.Downloading, 42);
        Assert.Equal(42, Field<ProgressBar>(window, "_progress").Value);
        Click(install);
        Assert.Equal(1, updates.PauseCalls);
        Assert.Equal("继续下载并安装", install.Content);
        Assert.Equal(ApplicationUpdateStatus.Paused, updates.Snapshot.Status);
        Click(install);
        Assert.Equal(2, updates.DownloadCalls);
        Assert.False(Field<MenuItem>(window, "_recheckItem").IsEnabled);
        Click(Field<MenuItem>(window, "_stopItem"));
        Assert.Equal(1, updates.StopCalls);
        Assert.Equal("下载并安装", install.Content);
        Click(install);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        Click(Field<Button>(window, "_backgroundButton"));
        Assert.True(closed);
        Assert.Equal(1, updates.PauseCalls);
        updates.CompleteDownload("update.exe", ApplicationUpdateStatus.Ready);
        Assert.Equal(1, updates.BeginInstallCalls);
        Assert.Equal(ApplicationUpdateStatus.Preparing, updates.Snapshot.Status);
    }

    private static void Closing_the_window_pauses_without_installing()
    {
        foreach (var state in new[] { ApplicationUpdateStatus.Downloading, ApplicationUpdateStatus.Verifying, ApplicationUpdateStatus.Ready })
        {
            using var updates = AvailableUpdates();
            var window = new UpdateWindow(updates, UiLanguage.English);
            Click(Field<Button>(window, "_installButton"));
            updates.SetStatus(state);
            window.Close();
            Assert.Equal(1, updates.PauseCalls);
            Assert.Equal(0, updates.BeginInstallCalls);
            var reopened = new UpdateWindow(updates, UiLanguage.English);
            Assert.Equal("Resume and install", Field<Button>(reopened, "_installButton").Content);
            Assert.Equal(1, updates.DownloadCalls);
            Assert.False(Field<MenuItem>(reopened, "_recheckItem").IsEnabled);
            reopened.Close();
            Assert.Equal(1, updates.PauseCalls);
        }
    }

    private static void Verification_can_pause_but_install_handoff_disables_controls()
    {
        using var updates = AvailableUpdates();
        var window = new UpdateWindow(updates, UiLanguage.Chinese);
        var install = Field<Button>(window, "_installButton");
        Click(install);
        updates.SetStatus(ApplicationUpdateStatus.Verifying);
        Assert.True(install.IsEnabled);
        Assert.Equal("暂停", install.Content);
        Assert.Equal("后台更新", Field<Button>(window, "_backgroundButton").Content);
        Assert.True(Field<ProgressBar>(window, "_progress").IsIndeterminate);
        Click(install);
        Assert.Equal(1, updates.PauseCalls);
        Click(install);
        updates.SetStatus(ApplicationUpdateStatus.Preparing);
        Assert.False(install.IsEnabled);
        Assert.False(Field<MenuItem>(window, "_stopItem").IsEnabled);
        window.Close();
        Assert.Equal(1, updates.PauseCalls);
        updates.CompleteDownload(null, ApplicationUpdateStatus.Preparing);

        using var failed = AvailableUpdates();
        failed.FailHandoff = true;
        var failedWindow = new UpdateWindow(failed, UiLanguage.English);
        try
        {
            var retry = Field<Button>(failedWindow, "_installButton");
            Click(retry);
            failed.CompleteDownload("update.exe", ApplicationUpdateStatus.Ready);
            Assert.Equal("Retry download and install", retry.Content);
            Assert.True(retry.IsEnabled);
        }
        finally { failedWindow.Close(); }
    }

    private static void Portable_and_current_copies_offer_only_valid_actions()
    {
        using var portable = new TestUpdateManager(new(ApplicationUpdateStatus.Available, "1.1.0", Release()), false);
        var portableWindow = new UpdateWindow(portable, UiLanguage.English);
        try
        {
            var action = Field<Button>(portableWindow, "_installButton");
            Assert.True(action.IsEnabled);
            Assert.Equal("Open release page", action.Content);
            Assert.Equal(UiText.Text(UiLanguage.English, "portableUpdateDescription"), Field<TextBlock>(portableWindow, "_statusText").Text);
            Click(action);
            Assert.Equal(1, portable.ReleasePageCalls);
            Assert.Equal(0, portable.DownloadCalls);
        }
        finally { portableWindow.Close(); }
        foreach (var release in new UpdateRelease?[] { null, Release() })
        {
            using var current = new TestUpdateManager(new(ApplicationUpdateStatus.UpToDate, "1.2.0", release));
            var window = new UpdateWindow(current, UiLanguage.English);
            Assert.False(Field<Button>(window, "_installButton").IsVisible);
            Assert.False(Field<MenuItem>(window, "_skipItem").IsVisible);
            window.Close();
        }
    }

    private static void Localized_windows_fit_and_release_notes_scroll()
    {
        var output = Environment.GetEnvironmentVariable("GHOST_UPDATE_OUTPUT");
        foreach (var language in new[] { UiLanguage.Chinese, UiLanguage.English })
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            using var updates = AvailableUpdates(longNotes: true);
            var window = new UpdateWindow(updates, language) { RequestedThemeVariant = theme, ShowActivated = false };
            window.Show();
            try
            {
                foreach (var minimum in new[] { false, true })
                {
                    window.Width = minimum ? window.MinWidth : 760;
                    window.Height = minimum ? window.MinHeight : 680;
                    Settle(window);
                    var notes = Field<StackPanel>(window, "_notesPanel");
                    var scroll = Assert.IsType<ScrollViewer>(notes.Parent);
                    Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                    foreach (var state in new[] { ApplicationUpdateStatus.Available, ApplicationUpdateStatus.Downloading, ApplicationUpdateStatus.Verifying, ApplicationUpdateStatus.Paused, ApplicationUpdateStatus.Error, ApplicationUpdateStatus.Preparing })
                    {
                        updates.SetStatus(state, 42);
                        Settle(window);
                        AssertFits(window, Field<Button>(window, "_installButton"));
                        AssertFits(window, Field<Button>(window, "_moreButton"));
                        AssertFits(window, Field<TextBlock>(window, "_statusText"));
                        Assert.True(scroll.Viewport.Height > 30);
                        Capture(window, output, $"update-{language}-{theme}-{(minimum ? "minimum" : "normal")}-{state}");
                    }
                    updates.SetStatus(ApplicationUpdateStatus.Available);
                }
                var more = Field<Button>(window, "_moreButton");
                Assert.True(more.Focus(NavigationMethod.Tab));
                more.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                Settle(window);
                Assert.True(more.Flyout!.IsOpen);
                Capture(window, output, $"update-{language}-{theme}-menu");
                more.Flyout.Hide();
            }
            finally { window.Close(); }

            var settings = new SettingsWindow(new AppSettings { Language = language }, _ => true,
                updates: updates, openUpdateWindow: () => { }, initialPage: "about")
            { RequestedThemeVariant = theme, ShowActivated = false };
            settings.Show();
            try
            {
                settings.Width = settings.MinWidth;
                settings.Height = settings.MinHeight;
                Field<NavigationView>(settings, "_navigation").IsPaneOpen = false;
                Settle(settings);
                var action = Field<Button>(settings, "_checkUpdatesButton");
                AssertFits(settings, action);
                AssertFits(settings, Field<TextBlock>(settings, "_lastUpdateCheckText"));
                AssertFits(settings, Field<TextBlock>(settings, "_updateStatusText"));
                Capture(settings, output, $"about-{language}-{theme}");
            }
            finally { settings.Close(); }
        }
    }

    private static void AssertFits(Window window, Control control)
    {
        if (!control.IsVisible) return;
        var point = control.TranslatePoint(default, window)!.Value;
        Assert.True(control.Bounds.Width > 0, control.GetType().Name);
        Assert.True(point.X >= 0 && point.Y >= 0 && point.X + control.Bounds.Width <= window.Bounds.Width + 1 &&
            point.Y + control.Bounds.Height <= window.Bounds.Height + 1, $"{control.GetType().Name}: {point}, {control.Bounds}, {window.Bounds}");
        foreach (var text in control.GetVisualDescendants().OfType<TextBlock>().Prepend(control as TextBlock).OfType<TextBlock>())
            Assert.All(text.TextLayout.TextLines, line => Assert.False(line.HasCollapsed, text.Text));
    }

    private static void Capture(Window window, string? output, string name)
    {
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)(window.Bounds.Width * scale), (int)(window.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
            bitmap.Render(window);
            bitmap.Save(Path.Combine(output, $"{name}-{scale * 100:0}.png"));
        }
    }

    private static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using var settled = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));
        try { Dispatcher.UIThread.MainLoop(settled.Token); }
        catch (OperationCanceledException) { }
        window.UpdateLayout();
    }

    private static UpdateRelease Release(bool longNotes = false)
    {
        var uri = new Uri("https://github.com/PomDetom/GhostSlacking/releases/tag/v1.2.0");
        UpdateAsset Asset(string name) => new(name, 12 * 1048576, new Uri($"https://github.com/PomDetom/GhostSlacking/releases/download/v1.2.0/{name}"), null);
        return new(ReleaseVersion.Parse("1.2.0"), "1.2.0", uri, Asset("setup.exe"), Asset("setup.exe.sha256"), Asset("release.json"), new string('A', 64),
            [new(ReleaseVersion.Parse("1.2.0"), "1.2.0", uri, new LocalizedReleaseNotes(
                "### 新功能\n- 显示更新内容。" + (longNotes ? string.Concat(Enumerable.Repeat("\n- 改进设置页面的布局、下载进度反馈和键盘操作。", 35)) : ""),
                "### Features\n- Show what's new." + (longNotes ? string.Concat(Enumerable.Repeat("\n- Improve settings layout, download feedback and keyboard navigation.", 35)) : "")))], true);
    }

    private static TestUpdateManager AvailableUpdates(bool longNotes = false) => new(new(ApplicationUpdateStatus.Available, "1.1.0", Release(longNotes)));
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private sealed class TestUpdateManager(ApplicationUpdateSnapshot snapshot, bool canInstall = true) : IApplicationUpdateManager
    {
        private TaskCompletionSource<string?>? _download;
        private TaskCompletionSource<ApplicationUpdateSnapshot>? _check;
        private CancellationTokenSource? _cancellation;
        public ApplicationUpdateSnapshot Snapshot { get; private set; } = snapshot;
        public bool CanInstallUpdates => canInstall;
        public UpdateChannel Channel { get; private set; } = UpdateChannel.Stable;
        public event EventHandler? Changed;
        public event EventHandler? InstallHandoffStarted;
        public int CheckCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public int BeginInstallCalls { get; private set; }
        public int ReleasePageCalls { get; private set; }
        public int PauseCalls { get; private set; }
        public int StopCalls { get; private set; }
        public bool FailHandoff { get; set; }
        public Task<ApplicationUpdateSnapshot> CheckAsync(CancellationToken cancellationToken = default)
        {
            CheckCalls++;
            _check = new();
            SetStatus(ApplicationUpdateStatus.Checking);
            return _check.Task;
        }
        public void CompleteCheck()
        {
            SetStatus(Snapshot.Release is null ? ApplicationUpdateStatus.NoCompatibleRelease : ApplicationUpdateStatus.Available);
            _check!.SetResult(Snapshot);
        }
        public Task<string?> DownloadInstallerAsync(CancellationToken cancellationToken = default)
        {
            DownloadCalls++;
            _download = new();
            SetStatus(ApplicationUpdateStatus.Downloading, 0);
            return _download.Task;
        }
        public async Task StartDownloadAndInstallAsync()
        {
            using var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            var path = await DownloadInstallerAsync(cancellation.Token);
            if (!cancellation.IsCancellationRequested && path is not null) BeginAutomaticInstall(path);
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
        }
        public Task PauseDownloadAsync()
        {
            PauseCalls++;
            _cancellation?.Cancel();
            SetStatus(ApplicationUpdateStatus.Paused, Snapshot.ProgressPercent);
            _download?.TrySetResult(null);
            return Task.CompletedTask;
        }
        public Task StopDownloadAsync()
        {
            StopCalls++;
            _cancellation?.Cancel();
            SetStatus(ApplicationUpdateStatus.Available);
            _download?.TrySetResult(null);
            return Task.CompletedTask;
        }
        public Task PrepareForExitAsync() => Task.CompletedTask;
        public void CompleteDownload(string? path, ApplicationUpdateStatus status)
        {
            SetStatus(status);
            _download!.TrySetResult(path);
        }
        public bool BeginAutomaticInstall(string installerPath)
        {
            BeginInstallCalls++;
            SetStatus(FailHandoff ? ApplicationUpdateStatus.Error : ApplicationUpdateStatus.Preparing);
            if (FailHandoff) return false;
            InstallHandoffStarted?.Invoke(this, EventArgs.Empty);
            return true;
        }
        public void SkipCurrentRelease() => SetStatus(ApplicationUpdateStatus.Skipped);
        public void ResumeCurrentRelease() => SetStatus(ApplicationUpdateStatus.Available);
        public void SetStatus(ApplicationUpdateStatus status, int? progress = null)
        {
            var task = status is ApplicationUpdateStatus.Downloading or ApplicationUpdateStatus.Verifying or ApplicationUpdateStatus.Paused or
                ApplicationUpdateStatus.Preparing or ApplicationUpdateStatus.Ready || status == ApplicationUpdateStatus.Error && Snapshot.HasDownloadTask;
            var total = Snapshot.Release?.Installer.Size ?? 1;
            Snapshot = Snapshot with { Status = status, ProgressPercent = progress, HasDownloadTask = task,
                Download = task ? new UpdateDownloadProgress(total * (progress ?? 0) / 100, total) : null };
            Changed?.Invoke(this, EventArgs.Empty);
        }
        public bool OpenSourceRepository() => true;
        public bool OpenReleasePage() { ReleasePageCalls++; return true; }
        public void SetChannel(UpdateChannel channel) => Channel = channel;
        public void Dispose() { }
    }
}
