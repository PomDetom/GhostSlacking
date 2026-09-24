using GhostSlacking.App;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using System.Drawing;
using System.Reflection;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App.Tests;

public sealed class AvaloniaStartupTests
{
    [Fact]
    public void Avalonia_platform_and_application_icon_initialize_on_sta_thread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Program.BuildAvaloniaApp().SetupWithoutStarting();
                Assert.NotNull(AppIcon.Instance);
                Assert.NotNull(AppIcon.TitleBarImage);
                var settingsWindow = new SettingsWindow(new AppSettings(), _ => true);
                Assert.Same(AppIcon.TitleBarImage, settingsWindow.Icon);
                AssertDefaultSettingsWindowLayout(settingsWindow);
                AssertPeekFrameRateSetting(settingsWindow);
                AssertAboutPage();
                AssertUpdateWindow();
                AssertThemePreviewLifecycle();
                AssertSavedSettingsExport();
                using var trayIcon = new TrayIcon
                {
                    Icon = AppIcon.Instance,
                    IsVisible = true,
                    ToolTipText = "GhostSlacking test"
                };
                var raisedNotificationHandles = new List<nint>();
                using var notifications = new AvaloniaNotificationService(
                    () => Point.Empty,
                    NullLogger.Instance,
                    handle =>
                    {
                        raisedNotificationHandles.Add(handle);
                        return NativeResult.Ok("test");
                    });
                notifications.Show("ready", UserNotificationSeverity.Info);
                notifications.Show("ready again", UserNotificationSeverity.Info);
                AssertNotificationWindowComposition(notifications);
                AssertNotificationWindowsGrowWithText();
                AssertChatTextAndCapsuleWrapping();
                AssertNotificationFadeAndPreview();
                AssertNotificationFadeInterruption();
                AssertNotificationStyleSettingSaves();
                AssertNotificationPreviewSaveAndCancel();
                AssertChatWindowHitTesting();
                AssertChatHistoryFitsTheWorkingArea();
                Assert.Equal(2, raisedNotificationHandles.Count);
                Assert.All(raisedNotificationHandles, handle => Assert.NotEqual(0, handle));
                trayIcon.IsVisible = false;
                settingsWindow.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }

    private static void AssertThemePreviewLifecycle()
    {
        var unsavedPreviews = new List<UiThemeMode>();
        var unsavedWindow = new SettingsWindow(new AppSettings(), _ => true, unsavedPreviews.Add);
        var unsavedTheme = GetPrivateField<ComboBox>(unsavedWindow, "_themeMode");
        unsavedTheme.SelectedIndex = 2;

        Assert.Equal(UiThemeMode.Dark, Assert.Single(unsavedPreviews));
        Assert.Equal(SettingsStatus.Modified, GetPrivateField<SettingsEditState>(unsavedWindow, "_editState").Status);

        unsavedWindow.Close();
        Assert.Equal(UiThemeMode.System, unsavedPreviews[^1]);

        var revertedPreviews = new List<UiThemeMode>();
        var revertedWindow = new SettingsWindow(new AppSettings(), _ => true, revertedPreviews.Add);
        var revertedTheme = GetPrivateField<ComboBox>(revertedWindow, "_themeMode");
        revertedTheme.SelectedIndex = 1;
        revertedTheme.SelectedIndex = 0;

        Assert.Equal([UiThemeMode.Light, UiThemeMode.System], revertedPreviews);
        Assert.Equal(SettingsStatus.None, GetPrivateField<SettingsEditState>(revertedWindow, "_editState").Status);
        revertedWindow.Close();

        var savedPreviews = new List<UiThemeMode>();
        var savedWindow = new SettingsWindow(new AppSettings(), _ => true, savedPreviews.Add);
        GetPrivateField<ComboBox>(savedWindow, "_themeMode").SelectedIndex = 2;
        InvokeSave(savedWindow);

        Assert.Equal(UiThemeMode.Dark, GetPrivateField<SettingsEditState>(savedWindow, "_editState").SavedSettings.ThemeMode);
        savedWindow.Close();
        Assert.Equal(UiThemeMode.Dark, savedPreviews[^1]);

        var failedPreviews = new List<UiThemeMode>();
        var failedWindow = new SettingsWindow(new AppSettings(), _ => false, failedPreviews.Add);
        GetPrivateField<ComboBox>(failedWindow, "_themeMode").SelectedIndex = 2;
        InvokeSave(failedWindow);

        Assert.Equal(SettingsStatus.Error, GetPrivateField<SettingsEditState>(failedWindow, "_editState").Status);
        failedWindow.Close();
        Assert.Equal(UiThemeMode.System, failedPreviews[^1]);

        var languagePreviews = new List<UiThemeMode>();
        var languageWindow = new SettingsWindow(new AppSettings(), _ => true, languagePreviews.Add);
        GetPrivateField<ComboBox>(languageWindow, "_language").SelectedIndex = 1;

        Assert.Empty(languagePreviews);
        languageWindow.Close();
    }

    private static void AssertSavedSettingsExport()
    {
        AppSettings? exported = null;
        var settingsWindow = new SettingsWindow(
            new AppSettings(),
            _ => true,
            exportSettings: (settings, _) =>
            {
                exported = settings;
                return Task.FromResult(DataExportResult.Success());
            });
        GetPrivateField<ComboBox>(settingsWindow, "_logLevel").SelectedIndex = 3;

        settingsWindow.ExportSavedSettingsAsync(Stream.Null).GetAwaiter().GetResult();

        Assert.NotNull(exported);
        Assert.Equal(LogLevel.Info, exported.MinimumLogLevel);
        Assert.Equal(SettingsStatus.Modified, GetPrivateField<SettingsEditState>(settingsWindow, "_editState").Status);
        settingsWindow.Close();
    }

    private static void AssertDefaultSettingsWindowLayout(SettingsWindow settingsWindow)
    {
        var navigation = GetPrivateField<NavigationView>(settingsWindow, "_navigation");
        var infoBar = GetPrivateField<InfoBar>(settingsWindow, "_infoBar");
        var exportLogs = GetPrivateField<Button>(settingsWindow, "_exportLogsButton");
        var exportSettings = GetPrivateField<Button>(settingsWindow, "_exportSettingsButton");
        var footerContent = Assert.IsType<Grid>(infoBar.Parent);
        var footer = Assert.IsType<Border>(footerContent.Parent);
        var content = Assert.IsAssignableFrom<Control>(settingsWindow.Content);

        Assert.Equal(settingsWindow.MinWidth, settingsWindow.Width);
        Assert.Equal(settingsWindow.MinHeight, settingsWindow.Height);
        Assert.False(navigation.IsPaneOpen);
        Assert.Equal(32, infoBar.Height);
        Assert.Equal(HorizontalAlignment.Left, infoBar.HorizontalAlignment);
        Assert.Equal("导出日志", exportLogs.Content);
        Assert.Equal("导出配置", exportSettings.Content);

        content.Measure(new Avalonia.Size(settingsWindow.Width, settingsWindow.Height));
        var closedHeight = footer.DesiredSize.Height;
        infoBar.IsOpen = true;
        content.Measure(new Avalonia.Size(settingsWindow.Width, settingsWindow.Height));
        Assert.Equal(closedHeight, footer.DesiredSize.Height);
        infoBar.IsOpen = false;
    }

    private static void AssertPeekFrameRateSetting(SettingsWindow settingsWindow)
    {
        var frameRate = GetPrivateField<ComboBox>(settingsWindow, "_peekFrameRateLimit");
        Assert.Equal(
            ["自动（最高 120 FPS）", "60 FPS", "90 FPS", "120 FPS"],
            frameRate.Items.Select(item => item?.ToString() ?? string.Empty).ToArray());
        Assert.Equal(0, frameRate.SelectedIndex);

        frameRate.SelectedIndex = 2;
        GetPrivateField<ComboBox>(settingsWindow, "_language").SelectedIndex = 1;

        Assert.Equal(2, frameRate.SelectedIndex);
        Assert.Equal("Auto (up to 120 FPS)", frameRate.Items[0]?.ToString());
        InvokeSave(settingsWindow);
        Assert.Equal(
            PeekFrameRateLimit.Fps90,
            GetPrivateField<SettingsEditState>(settingsWindow, "_editState").SavedSettings.PeekFrameRateLimit);
    }

    private static void AssertAboutPage()
    {
        var localDate = new DateTime(2026, 9, 10, 16, 30, 0, DateTimeKind.Unspecified);
        var lastSuccessfulCheck = new DateTimeOffset(localDate, TimeZoneInfo.Local.GetUtcOffset(localDate));
        var release = new UpdateRelease(
            ReleaseVersion.Parse("1.2.0"),
            "1.2.0",
            new Uri("https://github.com/PomDetom/GhostSlacking/releases/tag/v1.2.0"),
            Asset("GhostSlacking-1.2.0-win-x64.msi"),
            Asset("GhostSlacking-1.2.0-win-x64.msi.sha256"),
            Asset("release.json"),
            new string('A', 64),
            [
                new ReleaseNotesEntry(
                    ReleaseVersion.Parse("1.2.0"),
                    "1.2.0",
                    new Uri("https://github.com/PomDetom/GhostSlacking/releases/tag/v1.2.0"),
                    new LocalizedReleaseNotes("### 新功能\n- 显示更新内容。", "### Features\n- Show what's new."))
            ]);
        using var updates = new TestUpdateManager(new ApplicationUpdateSnapshot(
            ApplicationUpdateStatus.Available,
            "1.1.0",
            release,
            LastSuccessfulCheckUtc: lastSuccessfulCheck));
        var opened = false;
        var window = new SettingsWindow(
            new AppSettings(),
            _ => true,
            updates: updates,
            openUpdateWindow: () => opened = true,
            initialPage: "about");
        var navigation = GetPrivateField<NavigationView>(window, "_navigation");
        var aboutItem = GetPrivateField<NavigationViewItem>(window, "_aboutItem");
        var pageTitle = GetPrivateField<TextBlock>(window, "_pageTitle");
        var currentVersion = GetPrivateField<TextBlock>(window, "_currentVersionText");
        var latestVersion = GetPrivateField<TextBlock>(window, "_latestVersionText");
        var lastUpdateCheck = GetPrivateField<TextBlock>(window, "_lastUpdateCheckText");
        var updateStatus = GetPrivateField<TextBlock>(window, "_updateStatusText");
        var viewUpdate = GetPrivateField<Button>(window, "_viewUpdateButton");
        var sourceRepository = GetPrivateField<Button>(window, "_sourceRepositoryButton");
        var releasePage = GetPrivateField<Button>(window, "_releasePageButton");

        Assert.Contains(aboutItem, navigation.FooterMenuItems.Cast<object>());
        Assert.Equal("关于", aboutItem.Content);
        Assert.Equal("关于", pageTitle.Text);
        Assert.Equal("当前版本：1.1.0", currentVersion.Text);
        Assert.Equal("最新版本：1.2.0", latestVersion.Text);
        Assert.Equal("上次成功检查：2026-09-10 16:30", lastUpdateCheck.Text);
        Assert.True(viewUpdate.IsVisible);
        Assert.Equal("查看更新内容", viewUpdate.Content);
        viewUpdate.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(opened);
        Assert.Equal("GitHub 项目", sourceRepository.Content);
        Assert.Equal("发布页", releasePage.Content);
        var projectButtons = Assert.IsType<StackPanel>(sourceRepository.Parent);
        Assert.Same(projectButtons, releasePage.Parent);
        Assert.Same(sourceRepository, projectButtons.Children[0]);
        Assert.Same(releasePage, projectButtons.Children[1]);
        Assert.DoesNotContain("http", sourceRepository.Content?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", releasePage.Content?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SettingsStatus.None, GetPrivateField<SettingsEditState>(window, "_editState").Status);

        updates.SkipCurrentRelease();

        Assert.True(viewUpdate.IsVisible);
        Assert.Equal(SettingsStatus.None, GetPrivateField<SettingsEditState>(window, "_editState").Status);

        updates.SetStatus(ApplicationUpdateStatus.Error);

        Assert.Equal("上次成功检查：2026-09-10 16:30", lastUpdateCheck.Text);
        Assert.Equal(UiText.Text(UiLanguage.Chinese, "updateFailed"), updateStatus.Text);
        window.Close();

        using var uncheckedUpdates = new TestUpdateManager(new ApplicationUpdateSnapshot(
            ApplicationUpdateStatus.Idle,
            "1.1.0"));
        var uncheckedWindow = new SettingsWindow(
            new AppSettings { Language = UiLanguage.English },
            _ => true,
            updates: uncheckedUpdates,
            initialPage: "about");

        Assert.Equal(
            "No successful update check yet",
            GetPrivateField<TextBlock>(uncheckedWindow, "_lastUpdateCheckText").Text);
        uncheckedWindow.Close();
    }

    private static void AssertUpdateWindow()
    {
        var releaseUrl = new Uri("https://github.com/PomDetom/GhostSlacking/releases/tag/v1.2.0");
        var release = new UpdateRelease(
            ReleaseVersion.Parse("1.2.0"),
            "1.2.0",
            releaseUrl,
            Asset("GhostSlacking-1.2.0-win-x64.msi"),
            Asset("GhostSlacking-1.2.0-win-x64.msi.sha256"),
            Asset("release.json"),
            new string('A', 64),
            [
                new ReleaseNotesEntry(
                    ReleaseVersion.Parse("1.2.0"),
                    "1.2.0",
                    releaseUrl,
                    new LocalizedReleaseNotes("### 新功能\n- 显示更新内容。", "### Features\n- Show what's new."))
            ],
            ReleaseHistoryIncomplete: true);
        using var updates = new TestUpdateManager(new ApplicationUpdateSnapshot(
            ApplicationUpdateStatus.Available,
            "1.1.0",
            release),
            installerPath: "update.msi");
        var window = new UpdateWindow(updates, UiLanguage.Chinese);
        var notes = GetPrivateField<StackPanel>(window, "_notesPanel");
        var install = GetPrivateField<Button>(window, "_installButton");
        var skip = GetPrivateField<Button>(window, "_skipButton");
        var warning = GetPrivateField<TextBlock>(window, "_historyWarningText");

        Assert.Equal("从 1.1.0 更新到 1.2.0", GetPrivateField<TextBlock>(window, "_versionText").Text);
        Assert.Contains(notes.Children.OfType<TextBlock>(), text => text.Text == "• 显示更新内容。");
        Assert.True(warning.IsVisible);
        Assert.Equal("下载并安装", install.Content);
        Assert.Equal("跳过此版本", skip.Content);
        install.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, updates.DownloadCalls);
        Assert.Equal(1, updates.BeginInstallCalls);

        var skippedWindowClosed = false;
        window.Closed += (_, _) => skippedWindowClosed = true;
        skip.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(ApplicationUpdateStatus.Skipped, updates.Snapshot.Status);
        Assert.True(skippedWindowClosed);

        var resumeWindow = new UpdateWindow(updates, UiLanguage.Chinese);
        var resume = GetPrivateField<Button>(resumeWindow, "_skipButton");
        var resumedWindowClosed = false;
        resumeWindow.Closed += (_, _) => resumedWindowClosed = true;
        Assert.Equal("恢复提醒", resume.Content);
        resume.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(ApplicationUpdateStatus.Available, updates.Snapshot.Status);
        Assert.False(resumedWindowClosed);
        resumeWindow.RefreshLanguage(UiLanguage.English);
        Assert.Contains(
            GetPrivateField<StackPanel>(resumeWindow, "_notesPanel").Children.OfType<TextBlock>(),
            text => text.Text == "• Show what's new.");
        Assert.Equal("Later", GetPrivateField<Button>(resumeWindow, "_laterButton").Content);
        resumeWindow.Close();

        using var portableUpdates = new TestUpdateManager(new ApplicationUpdateSnapshot(
            ApplicationUpdateStatus.Available,
            "1.1.0",
            release),
            canInstallUpdates: false);
        var portableWindow = new UpdateWindow(portableUpdates, UiLanguage.English);
        Assert.False(GetPrivateField<Button>(portableWindow, "_installButton").IsEnabled);
        Assert.Equal(
            UiText.Text(UiLanguage.English, "portableUpdateDescription"),
            GetPrivateField<TextBlock>(portableWindow, "_statusText").Text);
        portableWindow.Close();
    }

    private static UpdateAsset Asset(string name) => new(
        name,
        1,
        new Uri($"https://github.com/PomDetom/GhostSlacking/releases/download/v1.2.0/{name}"),
        null);

    private static void AssertNotificationWindowComposition(AvaloniaNotificationService notifications)
    {
        var field = typeof(AvaloniaNotificationService).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic);
        var notificationWindow = Assert.IsAssignableFrom<Window>(field?.GetValue(notifications));
        var windowBackground = Assert.IsAssignableFrom<ISolidColorBrush>(notificationWindow.Background);
        var surface = Assert.IsType<Border>(notificationWindow.Content);

        Assert.Equal(Colors.Transparent, windowBackground.Color);
        Assert.Contains(WindowTransparencyLevel.Transparent, notificationWindow.TransparencyLevelHint);
        Assert.True(surface.ClipToBounds);
        Assert.Equal(new Avalonia.Thickness(0), surface.Margin);
        var accent = GetPrivateField<Border>(notificationWindow, "_accent");
        var body = GetPrivateField<Border>(notificationWindow, "_cardBody");
        Assert.Equal(4, accent.Width);
        Assert.Equal(new Avalonia.CornerRadius(8, 0, 0, 8), accent.CornerRadius);
        Assert.Equal(new Avalonia.Thickness(0, 1, 1, 1), body.BorderThickness);
        Assert.Equal(
            AvaloniaNotificationService.ResolvePalette(null, UserNotificationSeverity.Info).Surface,
            Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);
        Assert.Equal(AppTheme.AccentColor, Assert.IsAssignableFrom<ISolidColorBrush>(accent.Background).Color);

        ((NotificationWindow)notificationWindow).Update("error", UserNotificationSeverity.Error, false);
        Assert.Equal(
            AvaloniaNotificationService.ResolvePalette(null, UserNotificationSeverity.Error).Surface,
            Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);
        Assert.Equal(
            AvaloniaNotificationService.ResolvePalette(null, UserNotificationSeverity.Error).Accent,
            Assert.IsAssignableFrom<ISolidColorBrush>(accent.Background).Color);
    }

    private static void AssertNotificationWindowsGrowWithText()
    {
        foreach (var style in new[] { NotificationStyle.Card, NotificationStyle.Capsule, NotificationStyle.Chat })
        {
            var window = new NotificationWindow(style);
            window.Update("已隐藏", UserNotificationSeverity.Info, false);
            var shortWidth = window.Width;
            var shortHeight = window.Height;
            window.Update(string.Join(" ", Enumerable.Repeat("A longer notification message", 12)),
                UserNotificationSeverity.Error, false);

            Assert.True(window.Width > shortWidth, $"{style}: width {shortWidth} -> {window.Width}");
            Assert.True(window.Height > shortHeight, $"{style}: height {shortHeight} -> {window.Height}");
            Assert.InRange(window.Width, 1, style == NotificationStyle.Chat ? 430 : 370);
            window.Close();
        }
    }

    private static void AssertChatTextAndCapsuleWrapping()
    {
        var chat = new NotificationWindow(NotificationStyle.Chat);
        chat.Update("清晰的聊天消息", UserNotificationSeverity.Info, false);
        var chatText = GetPrivateField<TextBlock>(chat, "_message");
        var shadow = GetPrivateField<TextBlock>(chat, "_shadow");
        Assert.Equal(14, chatText.FontSize);
        Assert.Equal(FontWeight.SemiBold, chatText.FontWeight);
        Assert.Equal(14, shadow.FontSize);
        Assert.Equal(1.5, Assert.IsType<TranslateTransform>(shadow.RenderTransform).X);
        Assert.Equal(chatText.Text, shadow.Text);

        var longMessage = new string('长', 200);
        chat.Update(longMessage, UserNotificationSeverity.Info, false);
        var baselineWidth = chat.Width;
        var baselineHeight = chat.Height;
        chat.Update(longMessage, UserNotificationSeverity.Info, false, chatTextWidth: 600);
        var wideHeight = chat.Height;
        Assert.True(wideHeight < baselineHeight);
        chat.UpdateChat(longMessage, UserNotificationSeverity.Info, false,
            availableHeight: (baselineHeight + wideHeight) / 2,
            maximumTextWidth: 600);
        Assert.True(chat.Width > baselineWidth);
        Assert.True(chat.Width <= 606);
        Assert.True(chat.Height <= (baselineHeight + wideHeight) / 2);
        var chatSurface = GetPrivateField<Border>(chat, "_surface");
        chatSurface.Measure(new Avalonia.Size(chat.Width, chat.Height));
        Assert.True(chatSurface.DesiredSize.Height <= chat.Height);
        chat.Close();

        var capsule = new NotificationWindow(NotificationStyle.Capsule);
        capsule.Update("窗口已隐藏，请按快捷键查看当前窗口状态。窗口已隐藏，请按快捷键查看当前窗口状态。",
            UserNotificationSeverity.Info, false);
        var capsuleText = GetPrivateField<TextBlock>(capsule, "_message");
        var capsuleSurface = GetPrivateField<Border>(capsule, "_surface");
        Assert.Equal(new Avalonia.CornerRadius(12), capsuleSurface.CornerRadius);
        Assert.NotNull(capsuleText.Text);
        Assert.Contains('\n', capsuleText.Text);
        Assert.True(capsule.Width <= 322, $"Capsule width is {capsule.Width}");
        capsule.Close();
    }

    private static void AssertNotificationFadeAndPreview()
    {
        var now = DateTimeOffset.UtcNow;
        var card = new NotificationWindow(NotificationStyle.Card);
        card.Update("fade", UserNotificationSeverity.Info, false);
        card.BeginFadeIn(now, reset: true);
        Assert.Equal(0, card.Opacity);
        card.Show();
        card.AdvanceFade(now.Add(AvaloniaNotificationService.FadeInDuration));
        Assert.Equal(1, card.Opacity);
        card.BeginFadeOut(now.AddSeconds(2.3), AvaloniaNotificationService.FadeOutDuration);
        card.AdvanceFade(now.AddSeconds(2.4));
        Assert.InRange(card.Opacity, 0.7, 0.8);
        card.BeginFadeIn(now.AddSeconds(2.4), reset: true);
        Assert.Equal(0, card.Opacity);
        card.AdvanceFade(now.AddSeconds(2.4).Add(AvaloniaNotificationService.FadeInDuration));
        Assert.Equal(1, card.Opacity);
        card.BeginFadeOut(now.AddSeconds(2.5), AvaloniaNotificationService.FadeOutDuration);
        card.AdvanceFade(now.AddSeconds(2.5).Add(AvaloniaNotificationService.FadeOutDuration));
        Assert.False(card.IsVisible);
        card.Close();

        var preview = new NotificationWindow(NotificationStyle.Card, preview: true);
        preview.BeginFadeIn(now, reset: true);
        Assert.Equal(1, preview.Opacity);
        preview.Close();
    }

    private static void AssertNotificationFadeInterruption()
    {
        using var cardNotifications = new AvaloniaNotificationService(() => Point.Empty, NullLogger.Instance);
        cardNotifications.Show("first", UserNotificationSeverity.Info);
        var card = GetPrivateField<NotificationWindow>(cardNotifications, "_window");
        var now = DateTimeOffset.UtcNow.AddMilliseconds(-250);
        card.BeginFadeIn(now, reset: true);
        card.AdvanceFade(now.AddMilliseconds(150));
        card.BeginFadeOut(now.AddMilliseconds(150), AvaloniaNotificationService.FadeOutDuration);
        card.AdvanceFade(now.AddMilliseconds(250));
        Assert.True(card.IsFadingOut);
        cardNotifications.Show("second", UserNotificationSeverity.Info);
        Assert.False(card.IsFadingOut);
        Assert.InRange(card.Opacity, 0.7, 0.8);
        card.AdvanceFade(DateTimeOffset.UtcNow.Add(AvaloniaNotificationService.FadeInDuration));
        Assert.Equal(1, card.Opacity);
        cardNotifications.Show("third", UserNotificationSeverity.Info);
        Assert.Equal(1, card.Opacity);

        card.BeginFadeIn(now, reset: true);
        card.AdvanceFade(now.AddMilliseconds(75));
        var replacingOpacity = card.Opacity;
        cardNotifications.Show("fourth", UserNotificationSeverity.Info);
        Assert.Equal(replacingOpacity, card.Opacity);

        using var chatNotifications = new AvaloniaNotificationService(
            () => Point.Empty,
            NullLogger.Instance,
            settings: new AppSettings { NotificationStyle = NotificationStyle.Chat });
        chatNotifications.Show("first", UserNotificationSeverity.Info);
        var chatWindows = GetPrivateField<Dictionary<Guid, NotificationWindow>>(chatNotifications, "_chatWindows");
        var firstRow = Assert.Single(chatWindows.Values);
        Assert.Equal(1, firstRow.Opacity);
        now = DateTimeOffset.UtcNow.AddMilliseconds(-250);
        firstRow.BeginFadeOut(now.AddMilliseconds(150), AvaloniaNotificationService.FadeOutDuration);
        firstRow.AdvanceFade(now.AddMilliseconds(250));
        chatNotifications.Show("second", UserNotificationSeverity.Info);
        Assert.False(firstRow.IsFadingOut);
        Assert.Equal(1, firstRow.Opacity);
        Assert.Equal(1, Assert.Single(chatWindows.Values, window => window != firstRow).Opacity);
    }

    private static void AssertNotificationStyleSettingSaves()
    {
        AppSettings? saved = null;
        var window = new SettingsWindow(new AppSettings(), settings =>
        {
            saved = settings;
            return true;
        });
        GetPrivateField<ComboBox>(window, "_notificationStyle").SelectedIndex = 2;
        InvokeSave(window);

        Assert.Equal(NotificationStyle.Chat, saved?.NotificationStyle);
        window.Close();
    }

    private static void AssertNotificationPreviewSaveAndCancel()
    {
        AppSettings? saved = null;
        var window = new SettingsWindow(new AppSettings(), settings =>
        {
            saved = settings;
            return true;
        });
        var begin = typeof(SettingsWindow).GetMethod("BeginNotificationPositionPreview", BindingFlags.Instance | BindingFlags.NonPublic);
        var complete = typeof(SettingsWindow).GetMethod("CompleteNotificationPositionPreview", BindingFlags.Instance | BindingFlags.NonPublic);
        var cancel = typeof(SettingsWindow).GetMethod("CancelNotificationPositionPreview", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(begin);
        Assert.NotNull(complete);
        Assert.NotNull(cancel);

        begin.Invoke(window, null);
        var preview = GetPrivateField<NotificationWindow>(window, "_notificationPreview");
        var screen = preview.Screens.Primary;
        Assert.NotNull(screen);
        preview.Position = NotificationLayout.Position(
            screen.WorkingArea,
            screen.Scaling,
            new Avalonia.Size(preview.Width, preview.Height),
            new NotificationPlacement(0.3, 0.4));
        complete.Invoke(window, null);
        InvokeSave(window);
        Assert.NotNull(saved);
        Assert.InRange(saved.CardNotificationPlacement.X, 0.29, 0.31);
        Assert.InRange(saved.CardNotificationPlacement.Y, 0.39, 0.41);

        begin.Invoke(window, null);
        preview = GetPrivateField<NotificationWindow>(window, "_notificationPreview");
        preview.Position = NotificationLayout.Position(
            screen.WorkingArea,
            screen.Scaling,
            new Avalonia.Size(preview.Width, preview.Height),
            new NotificationPlacement(0.8, 0.9));
        cancel.Invoke(window, null);
        InvokeSave(window);
        Assert.InRange(saved.CardNotificationPlacement.X, 0.29, 0.31);
        Assert.InRange(saved.CardNotificationPlacement.Y, 0.39, 0.41);
        window.Close();
    }

    private static void AssertChatWindowHitTesting()
    {
        var clickThrough = new List<bool>();
        var platform = new Win32WindowApi();
        var clicks = 0;
        using var notifications = new AvaloniaNotificationService(
            () => Point.Empty,
            NullLogger.Instance,
            settings: new AppSettings { NotificationStyle = NotificationStyle.Chat },
            setClickThrough: (handle, enabled) =>
            {
                clickThrough.Add(enabled);
                return platform.SetNotificationClickThrough(handle, enabled);
            });

        notifications.Show("ordinary", UserNotificationSeverity.Info);
        var chatWindows = GetPrivateField<Dictionary<Guid, NotificationWindow>>(notifications, "_chatWindows");
        var ordinary = Assert.Single(chatWindows.Values);
        var ordinaryHandle = ordinary.TryGetPlatformHandle()?.Handle ?? 0;
        Assert.NotEqual(0, ordinaryHandle);
        Assert.Equal(1, ordinary.Opacity);
        Assert.NotEqual(0, Win32NativeMethods.GetWindowLongPtr(ordinaryHandle, Win32NativeMethods.GWL_EXSTYLE) &
            (nint)Win32NativeMethods.WS_EX_TRANSPARENT);

        notifications.Show("open update", UserNotificationSeverity.Info, () => clicks++);
        var actionable = Assert.Single(chatWindows.Values, window => window != ordinary);
        Assert.Equal(1, ordinary.Opacity);
        Assert.Equal(1, actionable.Opacity);
        var actionableHandle = actionable.TryGetPlatformHandle()?.Handle ?? 0;
        Assert.NotEqual(0, actionableHandle);
        Assert.Equal(0, Win32NativeMethods.GetWindowLongPtr(actionableHandle, Win32NativeMethods.GWL_EXSTYLE) &
            (nint)Win32NativeMethods.WS_EX_TRANSPARENT);

        Assert.Contains(true, clickThrough);
        Assert.Contains(false, clickThrough);

        var history = GetPrivateField<ChatNotificationQueue>(notifications, "_chat");
        var actionId = history.Entries[^1].Id;
        var fadeStart = DateTimeOffset.UtcNow.AddMilliseconds(-100);
        foreach (var window in chatWindows.Values)
        {
            window.BeginFadeOut(fadeStart, AvaloniaNotificationService.FadeOutDuration);
            window.AdvanceFade(fadeStart.AddMilliseconds(100));
        }

        var hoverMethod = typeof(AvaloniaNotificationService).GetMethod("OnChatPointerEntered", BindingFlags.Instance | BindingFlags.NonPublic);
        var exitMethod = typeof(AvaloniaNotificationService).GetMethod("OnChatPointerExited", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(hoverMethod);
        Assert.NotNull(exitMethod);
        hoverMethod.Invoke(notifications, [actionId]);
        Assert.True(history.IsPaused);
        Assert.All(chatWindows.Values, window => Assert.Equal(1, window.Opacity));
        exitMethod.Invoke(notifications, [actionId]);
        Assert.False(history.IsPaused);

        var clickMethod = typeof(AvaloniaNotificationService).GetMethod("OnChatClicked", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(clickMethod);
        clickMethod.Invoke(notifications, [actionId]);
        Assert.Equal(1, clicks);
        Assert.False(history.IsVisible);
        Assert.Equal(2, history.Entries.Count);
        Assert.All(chatWindows.Values, window => Assert.True(window.IsVisible));
        var fadeSample = DateTimeOffset.UtcNow.AddMilliseconds(100);
        Assert.All(chatWindows.Values, window => window.AdvanceFade(fadeSample));
        var fadingOpacities = chatWindows.Values.Select(window => window.Opacity).ToArray();
        Assert.All(fadingOpacities, opacity => Assert.InRange(opacity, 0.7, 0.8));
        Assert.InRange(fadingOpacities.Max() - fadingOpacities.Min(), 0, 0.02);
        Assert.All(chatWindows.Values, window => window.AdvanceFade(DateTimeOffset.UtcNow.AddMilliseconds(250)));
        Assert.All(chatWindows.Values, window => Assert.False(window.IsVisible));
        clickMethod.Invoke(notifications, [actionId]);
        Assert.Equal(1, clicks);

        notifications.Show("next", UserNotificationSeverity.Info);
        Assert.Equal(3, history.Entries.Count);
        Assert.True(history.IsVisible);
        Assert.All(chatWindows.Values, window => Assert.Equal(1, window.Opacity));
        Assert.NotEqual(0, Win32NativeMethods.GetWindowLongPtr(actionableHandle, Win32NativeMethods.GWL_EXSTYLE) &
            (nint)Win32NativeMethods.WS_EX_TRANSPARENT);
    }

    private static void AssertChatHistoryFitsTheWorkingArea()
    {
        using var notifications = new AvaloniaNotificationService(
            () => Point.Empty,
            NullLogger.Instance,
            settings: new AppSettings { NotificationStyle = NotificationStyle.Chat });
        for (var index = 0; index < ChatNotificationQueue.MaximumHistory; index++)
        {
            notifications.Show($"{index}: {new string('长', 500)}", UserNotificationSeverity.Info);
        }

        var history = GetPrivateField<ChatNotificationQueue>(notifications, "_chat");
        var windows = GetPrivateField<Dictionary<Guid, NotificationWindow>>(notifications, "_chatWindows");
        var first = windows[history.Entries[0].Id];
        var screen = first.Screens.ScreenFromPoint(new Avalonia.PixelPoint(0, 0)) ?? first.Screens.Primary;
        Assert.NotNull(screen);
        var gap = (int)Math.Ceiling(4 * screen.Scaling);
        var availableHeight = screen.WorkingArea.Height - 32;
        var heights = history.Entries
            .Select(entry => (int)Math.Ceiling(windows[entry.Id].Height * screen.Scaling))
            .ToArray();
        var expected = NotificationLayout.VisibleChatRows(heights, availableHeight, gap);

        Assert.Equal(ChatNotificationQueue.MaximumHistory, history.Entries.Count);
        Assert.Contains(history.Entries.Count - 1, expected);
        for (var index = 0; index < history.Entries.Count; index++)
        {
            var window = windows[history.Entries[index].Id];
            Assert.Equal(expected.Contains(index), window.IsVisible);
            if (window.IsVisible && heights[index] <= availableHeight)
            {
                Assert.InRange(window.Position.Y,
                    screen.WorkingArea.Y + 16,
                    screen.WorkingArea.Bottom - 16 - heights[index]);
            }
        }
    }

    private static T GetPrivateField<T>(object instance, string name) where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsType<T>(field?.GetValue(instance));
    }

    private static void InvokeSave(SettingsWindow settingsWindow)
    {
        var method = typeof(SettingsWindow).GetMethod("OnSaveClicked", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(settingsWindow, [null, new Avalonia.Interactivity.RoutedEventArgs()]);
    }

    private sealed class TestUpdateManager(
        ApplicationUpdateSnapshot snapshot,
        bool canInstallUpdates = true,
        string? installerPath = null) : IApplicationUpdateManager
    {
        public ApplicationUpdateSnapshot Snapshot { get; private set; } = snapshot;
        public bool CanInstallUpdates { get; } = canInstallUpdates;
        public UpdateChannel Channel { get; private set; } = UpdateChannel.Stable;
        public event EventHandler? Changed;
        public event EventHandler? InstallHandoffStarted;
        public int DownloadCalls { get; private set; }
        public int BeginInstallCalls { get; private set; }
        public Task<ApplicationUpdateSnapshot> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot);
        public Task<string?> DownloadInstallerAsync(CancellationToken cancellationToken = default)
        {
            DownloadCalls++;
            return Task.FromResult(installerPath);
        }
        public bool BeginAutomaticInstall(string installerPath)
        {
            BeginInstallCalls++;
            InstallHandoffStarted?.Invoke(this, EventArgs.Empty);
            return true;
        }
        public void SkipCurrentRelease()
        {
            Snapshot = Snapshot with { Status = ApplicationUpdateStatus.Skipped };
            Changed?.Invoke(this, EventArgs.Empty);
        }
        public void ResumeCurrentRelease()
        {
            Snapshot = Snapshot with { Status = ApplicationUpdateStatus.Available };
            Changed?.Invoke(this, EventArgs.Empty);
        }
        public void SetStatus(ApplicationUpdateStatus status)
        {
            Snapshot = Snapshot with { Status = status };
            Changed?.Invoke(this, EventArgs.Empty);
        }
        public bool OpenSourceRepository() => true;
        public bool OpenReleasePage() => true;
        public void SetChannel(UpdateChannel channel) => Channel = channel;
        public void Dispose() { }
    }
}
