using GhostSlacking.App;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using System.Drawing;
using System.Reflection;
using GhostSlacking.Core;
using GhostSlacking.Platform;
using Avalonia.VisualTree;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;

namespace GhostSlacking.App.Tests;

[CollectionDefinition("Windows UI", DisableParallelization = true)]
public sealed class WindowsUiCollection { }

[Collection("Windows UI")]
public sealed class AvaloniaStartupTests
{
    [Fact]
    public void Avalonia_platform_and_application_icon_initialize_on_sta_thread()
    {
        Exception? failure = null;
        var stage = "platform initialization";
        var thread = new Thread(() =>
        {
            try
            {
                Program.BuildAvaloniaApp().SetupWithoutStarting();
                stage = "settings and update windows";
                Assert.NotNull(AppIcon.Instance);
                Assert.NotNull(AppIcon.TitleBarImage);
                var settingsWindow = new SettingsWindow(new AppSettings(), _ => true);
                Assert.Same(AppIcon.TitleBarImage, settingsWindow.Icon);
                AssertDefaultSettingsWindowLayout(settingsWindow);
                stage = "experiment icon state and rendering";
                AssertExperimentIconRendering();
                AssertSaveBarFitsSingleLineMessages();
                AssertSettingsCancelAndSaveLifecycle();
                AssertPeekFrameRateSetting(settingsWindow);
                AssertBackgroundInteractionSetting();
                AssertAboutPage();
                AssertUpdateWindow();
                AssertThemePreviewLifecycle();
                AssertSavedSettingsExport();
                stage = "tray and notifications";
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
                stage = "HUD layout and lifecycle";
                AssertNotificationWindowsAdaptToContent();
                AssertNotificationMeasurementAcrossThemesAndDpi();
                AssertNotificationFadeAndPreview();
                AssertNotificationFadeInterruption();
                AssertNotificationHudSettingSavesAndCancels();
                AssertNotificationHitTesting();
                AssertPersistentBackgroundHud();
                Assert.Equal(2, raisedNotificationHandles.Count);
                Assert.All(raisedNotificationHandles, handle => Assert.NotEqual(0, handle));
                trayIcon.IsVisible = false;
                settingsWindow.Close();
                stage = "completed";
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // This checks many windows and notification layouts; installer handshakes
        // have separate deadlines and must not inherit this functional-test budget.
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), $"Avalonia UI probe timed out during {stage}.");
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
        var saveBar = GetPrivateField<SettingsSaveBar>(settingsWindow, "_saveBar");
        var exportLogs = GetPrivateField<Button>(settingsWindow, "_exportLogsButton");
        var exportSettings = GetPrivateField<Button>(settingsWindow, "_exportSettingsButton");
        var shell = Assert.IsType<Grid>(saveBar.Parent);
        var content = Assert.IsAssignableFrom<Control>(settingsWindow.Content);

        Assert.Equal(settingsWindow.MinWidth, settingsWindow.Width);
        Assert.Equal(settingsWindow.MinHeight, settingsWindow.Height);
        Assert.False(navigation.IsPaneOpen);
        Assert.Equal(56, navigation.CompactPaneLength);
        Assert.Equal(184, navigation.OpenPaneLength);
        Assert.Equal(new Avalonia.CornerRadius(12), settingsWindow.CornerRadius);
        Assert.Equal(new Avalonia.CornerRadius(12), navigation.Resources["NavigationViewContentGridCornerRadius"]);
        Assert.Equal(new Avalonia.Thickness(0, 0, 10d / 3, 10d / 3), navigation.Resources["NavigationViewContentMargin"]);
        Assert.False(saveBar.IsVisible);
        Assert.False(saveBar.IsEnabled);
        Assert.Equal(2, shell.RowDefinitions.Count);
        Assert.Equal(HorizontalAlignment.Center, saveBar.HorizontalAlignment);
        Assert.Equal("导出日志", exportLogs.Content);
        Assert.Equal("导出配置", exportSettings.Content);

        var size = new Avalonia.Size(settingsWindow.Width, settingsWindow.Height);
        content.Measure(size);
        content.Arrange(new Avalonia.Rect(size));
        var pageHost = GetPrivateField<ContentControl>(settingsWindow, "_pageHost");
        var viewportHeight = pageHost.Bounds.Height;
        var diameter = GetPrivateField<NumberBox>(settingsWindow, "_diameter");
        diameter.Value += 8;
        content.Measure(size);
        content.Arrange(new Avalonia.Rect(size));
        Assert.True(saveBar.IsVisible);
        Assert.Equal(viewportHeight, pageHost.Bounds.Height);
        diameter.Value -= 8;
        Assert.False(saveBar.IsEnabled);
    }

    private static void AssertExperimentIconRendering()
    {
        var window = new SettingsWindow(new AppSettings(), _ => true) { ShowActivated = false };
        var navigation = GetPrivateField<NavigationView>(window, "_navigation");
        var items = navigation.MenuItems.Cast<NavigationViewItem>().ToArray();
        var experiment = items[3];
        var reference = items[2];
        var directory = Environment.GetEnvironmentVariable("GHOST_ICON_OUTPUT");
        window.Show();
        try
        {
            foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark })
            foreach (var expanded in new[] { false, true })
            {
                window.RequestedThemeVariant = theme;
                navigation.IsPaneOpen = expanded;
                foreach (var state in new[] { "normal", "selected", "hover", "pressed", "disabled" })
                {
                    IBrush? referenceBrush = null;
                    foreach (var item in new[] { reference, experiment })
                    {
                        navigation.SelectedItem = state == "selected" ? item : items[0];
                        item.IsEnabled = state != "disabled";
                        ((IPseudoClasses)item.Classes).Set(":pointerover", state is "hover" or "pressed");
                        ((IPseudoClasses)item.Classes).Set(":pressed", state == "pressed");
                        window.UpdateLayout();
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        var icon = item == experiment
                            ? item.GetVisualDescendants().OfType<FAPathIcon>().Cast<Control>().First()
                            : item.GetVisualDescendants().OfType<SymbolIcon>().Cast<Control>().First();
                        var host = icon.GetVisualAncestors().OfType<ContentPresenter>().First(parent => parent.Name == "Icon");
                        var brush = icon.GetValue(TextElement.ForegroundProperty);
                        Assert.Equal(host.GetValue(TextElement.ForegroundProperty), brush);
                        Assert.Equal(1, icon.Opacity);
                        Assert.InRange(icon.Bounds.Width, 1, 24);
                        Assert.InRange(icon.Bounds.Height, 1, 24);
                        if (item == reference) referenceBrush = brush;
                        else Assert.Equal(referenceBrush, brush);
                        ((IPseudoClasses)item.Classes).Set(":pointerover", false);
                        ((IPseudoClasses)item.Classes).Set(":pressed", false);
                        item.IsEnabled = true;
                    }
                }
                navigation.SelectedItem = items[0];
                window.UpdateLayout();
                if (directory is null) continue;
                Directory.CreateDirectory(directory);
                foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
                {
                    using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
                        new Avalonia.PixelSize((int)(window.Bounds.Width * scale), (int)(window.Bounds.Height * scale)), new Avalonia.Vector(96 * scale, 96 * scale));
                    bitmap.Render(window);
                    bitmap.Save(Path.Combine(directory, $"icons-{theme}-{(expanded ? "open" : "compact")}-{(int)(scale * 100)}.png"));
                }
            }
        }
        finally { window.Close(); }
    }

    private static void AssertSaveBarFitsSingleLineMessages()
    {
        using var bar = new SettingsSaveBar(Brushes.White, Brushes.Gray, Brushes.Black) { MaxWidth = 600 };
        var host = new Window
        {
            Width = 680,
            Height = 240,
            Content = new Grid { Children = { bar } }
        };
        Avalonia.Size Measure()
        {
            host.UpdateLayout();
            return bar.Bounds.Size;
        }

        try
        {
            host.Show();
            bar.SetLanguage(UiLanguage.Chinese);
            bar.ShowMessage("未保存", "保存后生效", InfoBarSeverity.Warning, showActions: true);
            var shortMessage = Measure();
            bar.SetLanguage(UiLanguage.English);
            bar.ShowMessage("Unsaved changes", "Applies on save", InfoBarSeverity.Warning, showActions: true);
            var longMessage = Measure();
            Assert.True(longMessage.Width > shortMessage.Width, $"Short: {shortMessage}; long: {longMessage}");
            Assert.Equal(60, shortMessage.Height);
            Assert.Equal(shortMessage.Height, longMessage.Height);
            bar.ShowMessage("Save failed. Retry.", "Settings could not be saved. Check the log and configuration file permissions.",
                InfoBarSeverity.Error, showActions: true);
            Assert.Equal(60, Measure().Height);
            Assert.Contains("configuration file permissions", Assert.IsType<string>(Avalonia.Controls.ToolTip.GetTip(bar)));
            bar.ShowMessage("Saved", string.Empty, InfoBarSeverity.Success, showActions: false);
            var savedMessage = Measure();
            Assert.True(savedMessage.Width < longMessage.Width);
            Assert.Equal(60, savedMessage.Height);
        }
        finally
        {
            host.Close();
        }
    }

    private static void AssertSettingsCancelAndSaveLifecycle()
    {
        var saved = (new AppSettings
        {
            RevealDiameterPx = 232,
            RevealDiameterStepPx = 24,
            RevealSoftEdgeWidthPx = 20,
            Language = UiLanguage.English,
            ThemeMode = UiThemeMode.Dark,
            UpdateChannel = UpdateChannel.Test,
            NotificationStyle = NotificationStyle.Chat,
            CardNotificationPlacement = new NotificationPlacement(.2, .3),
            CapsuleNotificationPlacement = new NotificationPlacement(.4, .5),
            ChatNotificationPlacement = new NotificationPlacement(.6, .7)
        }).Normalize();
        var previews = new List<UiThemeMode>();
        var saveCalls = 0;
        var failSave = false;
        var window = new SettingsWindow(saved, _ => { saveCalls++; return !failSave; }, previews.Add);
        var bar = GetPrivateField<SettingsSaveBar>(window, "_saveBar");
        GetPrivateField<NumberBox>(window, "_diameter").Value = 320;
        GetPrivateField<NumberBox>(window, "_diameterStep").Value = 32;
        GetPrivateField<NumberBox>(window, "_softEdgeWidth").Value = 40;
        foreach (var name in new[] { "_blurLevel", "_shape", "_peekMode", "_peekFrameRateLimit", "_themeMode", "_logLevel", "_updateChannel", "_language" })
        {
            var combo = GetPrivateField<ComboBox>(window, name);
            combo.SelectedIndex = (combo.SelectedIndex + 1) % combo.Items.Count;
        }
        foreach (var name in new[] { "_restoreOnExit", "_startWithWindows", "_backgroundInteraction", "_continuousInteraction" })
        {
            var toggle = GetPrivateField<ToggleSwitch>(window, name);
            toggle.IsChecked = toggle.IsChecked != true;
        }
        Assert.Equal(0, GetPrivateField<ComboBox>(window, "_updateChannel").SelectedIndex);
        GetPrivateField<NumberBox>(window, "_notificationWidth").Value = 200;
        GetPrivateField<NumberBox>(window, "_notificationHeight").Value = 60;
        GetPrivateField<NumberBox>(window, "_notificationRadius").Value = 16;
        GetPrivateField<NumberBox>(window, "_notificationHorizontalPosition").Value = 20;
        GetPrivateField<NumberBox>(window, "_notificationVerticalPosition").Value = 30;
        typeof(SettingsWindow).GetField("_peekVirtualKey", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, 0x7B);
        var editors = (Array)typeof(SettingsWindow).GetMethod("GetHotkeyEditors", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null)!;
        for (var index = 0; index < editors.Length; index++)
        {
            var editor = editors.GetValue(index)!;
            editor.GetType().GetProperty("Binding")!.SetValue(editor, new HotkeyBinding(0x61 + index, ShortcutModifiers.Control));
        }

        GetPrivateField<Button>(bar, "_cancel").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var state = GetPrivateField<SettingsEditState>(window, "_editState");
        var current = (AppSettings)typeof(SettingsWindow).GetMethod("GetSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null)!;
        Assert.Equal(saved, current.Normalize());
        Assert.Equal(SettingsStatus.None, state.Status);
        Assert.False(bar.IsEnabled);
        Assert.Equal(UiThemeMode.Dark, previews[^1]);
        Assert.Equal(0, saveCalls);
        Assert.Equal("Cancel", GetPrivateField<Button>(bar, "_cancel").Content);

        var diameter = GetPrivateField<NumberBox>(window, "_diameter");
        diameter.Value = 240;
        GetPrivateField<Button>(bar, "_save").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(SettingsStatus.Saved, state.Status);
        Assert.False(GetPrivateField<StackPanel>(bar, "_actions").IsVisible);
        typeof(SettingsWindow).GetMethod("OnSavedStatusTimerTick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, [null, EventArgs.Empty]);
        Assert.Equal(SettingsStatus.None, state.Status);
        Assert.False(bar.IsEnabled);
        diameter.Value = 248;
        GetPrivateField<Button>(bar, "_cancel").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(240, diameter.Value);

        failSave = true;
        diameter.Value = 256;
        GetPrivateField<Button>(bar, "_save").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(SettingsStatus.Error, state.Status);
        Assert.Equal(256, diameter.Value);
        Assert.Equal(240, state.SavedSettings.RevealDiameterPx);
        Assert.True(GetPrivateField<StackPanel>(bar, "_actions").IsVisible);
        failSave = false;
        GetPrivateField<Button>(bar, "_save").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(256, state.SavedSettings.RevealDiameterPx);
        window.Close();
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

    private static void AssertBackgroundInteractionSetting()
    {
        AppSettings? saved = null;
        var window = new SettingsWindow(new AppSettings(), settings => { saved = settings; return true; });
        var toggle = GetPrivateField<ToggleSwitch>(window, "_backgroundInteraction");
        Assert.False(toggle.IsChecked == true);
        var continuous = GetPrivateField<ToggleSwitch>(window, "_continuousInteraction");
        Assert.False(continuous.IsChecked == true);
        Assert.False(continuous.IsEnabled);
        var navigation = GetPrivateField<NavigationView>(window, "_navigation");
        var items = navigation.MenuItems.Cast<NavigationViewItem>().ToArray();
        Assert.Equal("general", items[2].Tag);
        Assert.Equal("experiments", items[3].Tag);
        var flask = Assert.IsType<PathIconSource>(items[3].IconSource);
        Assert.False(flask.IsSet(IconSource.ForegroundProperty));
        var editor = typeof(SettingsWindow).GetField("_backgroundInteractionHotkey", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        var binding = editor.GetType().GetProperty("Binding")!;
        Assert.Equal(HotkeyBinding.Disabled, binding.GetValue(editor));
        toggle.IsChecked = true;
        Assert.True(continuous.IsEnabled);
        continuous.IsChecked = true;
        binding.SetValue(editor, new HotkeyBinding(0x49, ShortcutModifiers.Control | ShortcutModifiers.Shift));
        GetPrivateField<ComboBox>(window, "_language").SelectedIndex = 1;
        InvokeSave(window);
        Assert.NotNull(saved);
        Assert.True(saved.ExperimentalBackgroundInteraction);
        Assert.True(saved.ContinuousBackgroundInteraction);
        Assert.Equal(new HotkeyBinding(0x49, ShortcutModifiers.Control | ShortcutModifiers.Shift), saved.BackgroundInteractionHotkey);
        Assert.NotEqual("backgroundInteractionDescription", UiText.Text(UiLanguage.English, "backgroundInteractionDescription"));
        Assert.NotEqual("backgroundInteractionDescription", UiText.Text(UiLanguage.Chinese, "backgroundInteractionDescription"));
        window.Close();
    }

    private static void AssertPersistentBackgroundHud()
    {
        using var service = new AvaloniaNotificationService(() => Point.Empty, NullLogger.Instance);
        service.ShowPersistent("BG input");
        var state = GetPrivateField<TransientNotificationState>(service, "_state");
        state.Show("BG input", UserNotificationSeverity.Info, DateTimeOffset.UtcNow.AddHours(-1));
        typeof(AvaloniaNotificationService).GetMethod("OnTimerTick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(service, [null, EventArgs.Empty]);
        Assert.NotNull(state.Current);
        service.PauseForHover();
        Assert.NotNull(state.Current);
        var hud = GetPrivateField<NotificationWindow>(service, "_window");
        Assert.False(hud.ShowActivated);
        service.Hide();
        Assert.Null(state.Current);
        Assert.False(hud.IsVisible);
    }

    private static void AssertAboutPage()
    {
        var localDate = new DateTime(2026, 9, 10, 16, 30, 0, DateTimeKind.Unspecified);
        var lastSuccessfulCheck = new DateTimeOffset(localDate, TimeZoneInfo.Local.GetUtcOffset(localDate));
        var release = new UpdateRelease(
            ReleaseVersion.Parse("1.2.0"),
            "1.2.0",
            new Uri("https://github.com/PomDetom/GhostSlacking/releases/tag/v1.2.0"),
            Asset("GhostSlacking-1.2.0-win-x64-setup.exe"),
            Asset("GhostSlacking-1.2.0-win-x64-setup.exe.sha256"),
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
            Asset("GhostSlacking-1.2.0-win-x64-setup.exe"),
            Asset("GhostSlacking-1.2.0-win-x64-setup.exe.sha256"),
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
            installerPath: "update.exe");
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
        updates.SetStatus(ApplicationUpdateStatus.Preparing);
        Assert.False(install.IsEnabled);
        Assert.False(skip.IsEnabled);
        Assert.Equal(UiText.Text(UiLanguage.Chinese, "updatePreparing"), GetPrivateField<TextBlock>(window, "_statusText").Text);
        updates.SetStatus(ApplicationUpdateStatus.Available);
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
        var window = GetPrivateField<NotificationWindow>(notifications, "_window");
        var surface = GetPrivateField<Border>(window, "_surface");
        Assert.False(window.ShowActivated);
        Assert.False(window.ShowInTaskbar);
        Assert.Contains(WindowTransparencyLevel.Transparent, window.TransparencyLevelHint);
        Assert.InRange(surface.Width, 64, 132);
        Assert.Equal(44, surface.Height);
        Assert.Equal(new Avalonia.CornerRadius(10), surface.CornerRadius);
        var neutral = Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color;
        window.Update("恢复未成功", UserNotificationSeverity.Error, false);
        Assert.Equal(neutral, Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);
        Assert.Equal(AppTheme.Palette(window.ActualThemeVariant).Border,
            Assert.IsAssignableFrom<ISolidColorBrush>(surface.BorderBrush).Color);
    }

    private static void AssertNotificationWindowsAdaptToContent()
    {
        var window = new NotificationWindow();
        var surface = GetPrivateField<Border>(window, "_surface");
        var text = GetPrivateField<TextBlock>(window, "_message");
        var icon = GetPrivateField<Avalonia.Controls.Shapes.Path>(window, "_icon");
        var content = GetPrivateField<Grid>(window, "_content");
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, content.HorizontalAlignment);
        Assert.Equal(9, content.ColumnDefinitions[1].Width.Value);
        Assert.Equal(GridUnitType.Auto, content.ColumnDefinitions[2].Width.GridUnitType);
        window.Update("交互中", UserNotificationSeverity.Info, false);
        window.Configure(new NotificationHudSettings());
        Assert.InRange(surface.Width, 64, 99);
        Assert.Equal(13, text.FontSize);
        window.Measure(new Avalonia.Size(window.Width, window.Height));
        window.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
        Assert.True(content.Bounds.Width < 110);
        Assert.InRange(Math.Abs(content.Bounds.Center.X - surface.Bounds.Width / 2), 0, 1);
        foreach (var severity in Enum.GetValues<UserNotificationSeverity>())
        foreach (var message in new[] { "程序已启动", "已隐藏窗口", "发现新版本", "恢复未成功", new string('长', 500) })
        {
            window.Update(message, severity, false);
            window.Configure(new NotificationHudSettings());
            if (message.Length == 500) Assert.Equal(132, surface.Width);
            Assert.InRange(surface.Width, 64, 132);
            Assert.Equal(44, surface.Height);
            Assert.Equal(18, icon.Width);
            Assert.True(double.IsNaN(text.Width));
            Assert.InRange(text.FontSize, 11, 13);
            Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
        }
        window.Configure(new NotificationHudSettings { Width = 100, Height = 32 });
        foreach (var language in Enum.GetValues<UiLanguage>())
        foreach (var key in new[] { "backgroundStarting", "backgroundPending", "backgroundActive", "backgroundStopped",
                     "backgroundForegroundChanged", "backgroundUnsupported", "backgroundFailed", "backgroundCleanupFailed",
                     "backgroundUnavailable", "backgroundToggleRequired", "backgroundClickEditor", "backgroundTextOnly",
                     "backgroundImeRejected", "backgroundDeadKeyRejected", "backgroundSystemKeyRejected", "backgroundMouseRejected", "backgroundFocusFirst", "backgroundDiscoveryRejected" })
        {
            window.Update(UiText.Text(language, key), UserNotificationSeverity.Info, false);
            window.Configure(new NotificationHudSettings { Width = 100, Height = 32 });
            var measure = new TextBlock
            {
                Text = text.Text, FontFamily = text.FontFamily, FontWeight = text.FontWeight,
                FontSize = text.FontSize, LetterSpacing = text.LetterSpacing
            };
            measure.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.InRange(text.FontSize, 11, 13);
            Assert.True(measure.DesiredSize.Width <= surface.Width - 49 + 0.5, $"{language}/{key}: {measure.DesiredSize.Width}");
            Assert.InRange(surface.Width, 64, 100);
            Assert.Equal(32, surface.Height);
        }
        window.Close();
    }

    private static void AssertNotificationFadeAndPreview()
    {
        var window = new NotificationWindow();
        window.Update("程序已启动", UserNotificationSeverity.Info, false);
        var now = DateTimeOffset.UtcNow;
        window.BeginFadeIn(now, reset: true);
        window.Show();
        window.AdvanceFade(now.AddMilliseconds(150));
        Assert.Equal(1, window.Opacity);
        window.BeginFadeOut(now.AddSeconds(2), AvaloniaNotificationService.FadeOutDuration);
        window.AdvanceFade(now.AddSeconds(2.2));
        Assert.False(window.IsVisible);
        window.Close();
        var preview = new NotificationWindow(preview: true);
        preview.BeginFadeIn(now, reset: true);
        Assert.Equal(1, preview.Opacity);
        Assert.False(preview.ShowActivated);
        preview.Close();
    }

    private static void AssertNotificationMeasurementAcrossThemesAndDpi()
    {
        var settings = new NotificationHudSettings();
        var window = new NotificationWindow(preview: true);
        var surface = GetPrivateField<Border>(window, "_surface");
        foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark })
        foreach (var scaling in new[] { 1, 1.25, 1.5, 2 })
        {
            window.RequestedThemeVariant = theme;
            window.Update("交互中", UserNotificationSeverity.Info, false);
            var shortWidth = window.MeasurePreferredWidth(settings, scaling);
            Assert.Equal(shortWidth, window.MeasurePreferredWidth(settings, scaling));
            var geometry = NotificationLayout.Calculate(new Avalonia.PixelRect(-1920, 0, 1920, 1080), scaling, settings, shortWidth);
            window.Configure(settings, geometry);
            Assert.InRange(surface.Width, 64, 99);
            Assert.Equal(13, GetPrivateField<TextBlock>(window, "_message").FontSize);
            window.Update(new string('长', 100), UserNotificationSeverity.Info, false);
            var longWidth = window.MeasurePreferredWidth(settings, scaling);
            Assert.True(longWidth > shortWidth);
            window.Configure(settings, NotificationLayout.Calculate(new Avalonia.PixelRect(-1920, 0, 1920, 1080), scaling, settings, longWidth));
            Assert.Equal(132, surface.Width);
        }
        Assert.Equal("最大宽度", UiText.Text(UiLanguage.Chinese, "notificationWidth"));
        Assert.Equal("Maximum width", UiText.Text(UiLanguage.English, "notificationWidth"));
        window.Close();
    }

    private static void AssertNotificationFadeInterruption()
    {
        using var notifications = new AvaloniaNotificationService(() => Point.Empty, NullLogger.Instance,
            settings: new AppSettings { NotificationStyle = NotificationStyle.Chat });
        notifications.Show("程序已启动", UserNotificationSeverity.Info);
        var window = GetPrivateField<NotificationWindow>(notifications, "_window");
        var now = DateTimeOffset.UtcNow.AddMilliseconds(-250);
        window.BeginFadeIn(now, reset: true);
        window.AdvanceFade(now.AddMilliseconds(150));
        window.BeginFadeOut(now.AddMilliseconds(150), AvaloniaNotificationService.FadeOutDuration);
        window.AdvanceFade(now.AddMilliseconds(250));
        notifications.Show("已隐藏窗口", UserNotificationSeverity.Success);
        Assert.False(window.IsFadingOut);
        Assert.Same(window, GetPrivateField<NotificationWindow>(notifications, "_window"));
        window.AdvanceFade(DateTimeOffset.UtcNow.AddMilliseconds(150));
        Assert.Equal(1, window.Opacity);
        notifications.Show("恢复未成功", UserNotificationSeverity.Error);
        Assert.Equal(1, window.Opacity);
    }

    private static void AssertNotificationHudSettingSavesAndCancels()
    {
        AppSettings? saved = null;
        var failSave = true;
        var original = new AppSettings { NotificationStyle = NotificationStyle.Chat };
        var window = new SettingsWindow(original, settings => { if (failSave) return false; saved = settings; return true; });
        GetPrivateField<NumberBox>(window, "_notificationRadius").Value = 16;
        GetPrivateField<NumberBox>(window, "_notificationWidth").Value = 180;
        GetPrivateField<NumberBox>(window, "_notificationHeight").Value = 52;
        GetPrivateField<NumberBox>(window, "_notificationHorizontalPosition").Value = 20;
        GetPrivateField<NumberBox>(window, "_notificationVerticalPosition").Value = 30;
        typeof(SettingsWindow).GetMethod("BeginNotificationPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        var preview = GetPrivateField<NotificationWindow>(window, "_notificationPreview");
        Assert.True(GetPrivateField<Avalonia.Threading.DispatcherTimer>(window, "_notificationPreviewTimer").IsEnabled);
        Assert.InRange(GetPrivateField<Border>(preview, "_surface").Width, 64, 180);
        GetPrivateField<NumberBox>(window, "_notificationWidth").Value = 200;
        Assert.InRange(GetPrivateField<Border>(preview, "_surface").Width, 64, 200);
        using (var runtime = new AvaloniaNotificationService(() => Point.Empty, NullLogger.Instance,
            settings: new AppSettings { NotificationHud = new NotificationHudSettings { Width = 200, Height = 52, CornerRadius = 16 } }))
        {
            runtime.Show(UiText.Text(UiLanguage.Chinese, "notificationPreviewMessage"), UserNotificationSeverity.Info);
            var runtimeWindow = GetPrivateField<NotificationWindow>(runtime, "_window");
            Assert.Equal(GetPrivateField<Border>(preview, "_surface").Width, GetPrivateField<Border>(runtimeWindow, "_surface").Width);
        }
        InvokeSave(window);
        Assert.Null(saved);
        Assert.False(GetPrivateField<Avalonia.Threading.DispatcherTimer>(window, "_notificationPreviewTimer").IsEnabled);
        Assert.Equal(200, GetPrivateField<NumberBox>(window, "_notificationWidth").Value);
        failSave = false;
        InvokeSave(window);
        Assert.NotNull(saved);
        Assert.Equal(new NotificationHudSettings { CornerRadius = 16, Width = 200, Height = 52, HorizontalPosition = 20, VerticalPosition = 30 }, saved.NotificationHud);
        Assert.Equal(original.NotificationStyle, saved.NotificationStyle);
        GetPrivateField<NumberBox>(window, "_notificationWidth").Value = 250;
        var bar = GetPrivateField<SettingsSaveBar>(window, "_saveBar");
        GetPrivateField<Button>(bar, "_cancel").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(200, GetPrivateField<NumberBox>(window, "_notificationWidth").Value);
        Assert.Null(typeof(SettingsWindow).GetField("_notificationPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
        window.Close();
    }

    private static void AssertNotificationHitTesting()
    {
        var platform = new Win32WindowApi();
        var foreground = GetForegroundWindow();
        using var notifications = new AvaloniaNotificationService(() => Point.Empty, NullLogger.Instance,
            bringToFront: platform.BringToTopWithoutActivation,
            setClickThrough: platform.SetNotificationClickThrough);
        notifications.Show("程序已启动", UserNotificationSeverity.Info);
        var window = GetPrivateField<NotificationWindow>(notifications, "_window");
        var handle = window.TryGetPlatformHandle()!.Handle;
        var style = Win32NativeMethods.GetWindowLongPtr(handle, Win32NativeMethods.GWL_EXSTYLE);
        Assert.NotEqual(0, style & (nint)Win32NativeMethods.WS_EX_TRANSPARENT);
        Assert.NotEqual(0, style & (nint)Win32NativeMethods.WS_EX_NOACTIVATE);
        Assert.Equal(foreground, GetForegroundWindow());
        var clicks = 0;
        notifications.Show("发现新版本", UserNotificationSeverity.Info, () => clicks++);
        style = Win32NativeMethods.GetWindowLongPtr(handle, Win32NativeMethods.GWL_EXSTYLE);
        Assert.Equal(0, style & (nint)Win32NativeMethods.WS_EX_TRANSPARENT);
        Assert.NotEqual(0, style & (nint)Win32NativeMethods.WS_EX_NOACTIVATE);
        Assert.Equal(foreground, GetForegroundWindow());
        notifications.PauseForHover();
        Assert.True(GetPrivateField<TransientNotificationState>(notifications, "_state").IsPaused);
        notifications.Configure(new AppSettings { NotificationHud = new NotificationHudSettings { Width = 200 } });
        Assert.InRange(GetPrivateField<Border>(window, "_surface").Width, 64, 200);
        Assert.True(GetPrivateField<TransientNotificationState>(notifications, "_state").IsPaused);
        notifications.ResumeAfterHover();
        Assert.False(GetPrivateField<TransientNotificationState>(notifications, "_state").IsPaused);
        var click = typeof(AvaloniaNotificationService).GetMethod("OnWindowClicked", BindingFlags.Instance | BindingFlags.NonPublic)!;
        click.Invoke(notifications, [window, EventArgs.Empty]);
        click.Invoke(notifications, [window, EventArgs.Empty]);
        Assert.Equal(1, clicks);
        Assert.Null(GetPrivateField<TransientNotificationState>(notifications, "_state").Current);
        notifications.Show("发现新版本", UserNotificationSeverity.Info, () => clicks++);
        GetPrivateField<TransientNotificationState>(notifications, "_state").Show(
            "expired", UserNotificationSeverity.Info, DateTimeOffset.UtcNow.AddSeconds(-10), () => clicks++);
        notifications.PauseForHover();
        Assert.False(window.IsVisible);
        click.Invoke(notifications, [window, EventArgs.Empty]);
        Assert.Equal(1, clicks);

        var unsafeRaiseCalls = 0;
        using var failed = new AvaloniaNotificationService(() => Point.Empty, NullLogger.Instance,
            bringToFront: _ => { unsafeRaiseCalls++; return NativeResult.Ok("test"); },
            setClickThrough: (_, _) => NativeResult.Failed("test", 5, "denied"));
        failed.Show("程序已启动", UserNotificationSeverity.Info);
        Assert.False(GetPrivateField<NotificationWindow>(failed, "_window").IsVisible);
        Assert.Null(GetPrivateField<TransientNotificationState>(failed, "_state").Current);
        Assert.Equal(0, unsafeRaiseCalls);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

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
