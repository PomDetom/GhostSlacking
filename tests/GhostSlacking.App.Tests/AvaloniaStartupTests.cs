using GhostSlacking.App;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using System.Drawing;
using System.Reflection;
using GhostSlacking.Core;

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
                AssertThemePreviewLifecycle();
                AssertSavedSettingsExport();
                using var trayIcon = new TrayIcon
                {
                    Icon = AppIcon.Instance,
                    IsVisible = true,
                    ToolTipText = "GhostSlacking test"
                };
                using var notifications = new AvaloniaNotificationService(() => Point.Empty, NullLogger.Instance);
                notifications.Show("ready", UserNotificationSeverity.Info);
                AssertNotificationWindowComposition(notifications);
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

    private static void AssertNotificationWindowComposition(AvaloniaNotificationService notifications)
    {
        var field = typeof(AvaloniaNotificationService).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic);
        var notificationWindow = Assert.IsAssignableFrom<Window>(field?.GetValue(notifications));
        var windowBackground = Assert.IsAssignableFrom<ISolidColorBrush>(notificationWindow.Background);
        var surface = Assert.IsType<Border>(notificationWindow.Content);

        Assert.Equal(Colors.Transparent, windowBackground.Color);
        Assert.Contains(WindowTransparencyLevel.Transparent, notificationWindow.TransparencyLevelHint);
        Assert.True(surface.ClipToBounds);
        Assert.Equal(new Avalonia.Thickness(1), surface.Margin);
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
}
