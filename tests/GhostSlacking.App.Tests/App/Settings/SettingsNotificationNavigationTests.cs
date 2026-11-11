using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using GhostSlacking.App;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

internal static class SettingsNotificationNavigationTests
{
    // Called from the solution's single Avalonia STA fixture.
    internal static void VerifyUi()
    {
        var initial = new AppSettings { NotificationStyle = NotificationStyle.Chat };
        AppSettings? saved = null;
        var failSave = true;
        var saveCalls = 0;
        var window = new SettingsWindow(initial, settings =>
        {
            saveCalls++;
            if (failSave) return false;
            saved = settings;
            return true;
        }) { ShowActivated = false };
        window.Show();
        try
        {
            var navigation = Field<NavigationView>(window, "_navigation");
            var items = navigation.MenuItems.Cast<NavigationViewItem>().ToArray();
            Assert.Equal(new[] { "reveal", "hotkeys", "general", "notifications", "experiments" },
                items.Select(item => (string)item.Tag!).ToArray());
            Assert.Equal("reveal", Assert.IsType<NavigationViewItem>(navigation.SelectedItem).Tag);
            Assert.Equal("about", Assert.Single(navigation.FooterMenuItems.Cast<NavigationViewItem>()).Tag);
            var notificationItem = items.Single(item => Equals(item.Tag, "notifications"));
            var generalItem = items.Single(item => Equals(item.Tag, "general"));
            var pages = Field<Dictionary<string, Control>>(window, "_pages");
            var adjustments = Field<Dictionary<AdjustmentKind, (TextBlock Value, Button Row)>>(window, "_adjustmentValues");
            var kinds = new[] { AdjustmentKind.NotificationRadius, AdjustmentKind.NotificationWidth,
                AdjustmentKind.NotificationHeight, AdjustmentKind.NotificationHorizontal, AdjustmentKind.NotificationVertical };
            var notificationRows = pages["notifications"].GetLogicalDescendants().OfType<Button>().ToArray();
            Assert.Equal(kinds.Select(kind => adjustments[kind].Row), notificationRows);
            foreach (var row in notificationRows)
            {
                Assert.DoesNotContain(row, pages["general"].GetLogicalDescendants());
            }
            Assert.Contains(Field<ComboBox>(window, "_updateChannel"), pages["about"].GetLogicalDescendants());
            Assert.DoesNotContain(Field<ComboBox>(window, "_updateChannel"), pages["notifications"].GetLogicalDescendants());

            navigation.SelectedItem = notificationItem;
            Assert.Same(pages["notifications"], Field<ContentControl>(window, "_pageHost").Content);
            Assert.Equal(SettingsStatus.None, Field<SettingsEditState>(window, "_editState").Status);
            VerifyLocalizedLayout(window, navigation, notificationItem);
            Click(Field<SettingsSaveBar>(window, "_saveBar"), "_cancel");
            Assert.Same(notificationItem, navigation.SelectedItem);
            Assert.Equal(initial.Normalize(), Current(window).Normalize());

            adjustments[AdjustmentKind.NotificationWidth].Row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var discarded = window.ActiveAdjustment!;
            discarded.NumberEditor!.Value = 200;
            navigation.SelectedItem = generalItem;
            Assert.False(discarded.IsOpen);
            Assert.Null(window.ActiveAdjustment);
            Assert.Equal(initial.NotificationHud, Current(window).NotificationHud);
            Assert.Equal(SettingsStatus.None, Field<SettingsEditState>(window, "_editState").Status);

            navigation.SelectedItem = notificationItem;
            adjustments[AdjustmentKind.NotificationWidth].Row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.ActiveAdjustment!.NumberEditor!.Value = 200;
            window.ActiveAdjustment.Complete();
            Assert.Equal(200, Current(window).NotificationHud.Width);
            Assert.Equal(0, saveCalls);
            navigation.SelectedItem = generalItem;
            navigation.SelectedItem = notificationItem;
            Assert.Equal(200, Current(window).NotificationHud.Width);
            Field<NumberBox>(window, "_diameter").Value = 240;
            Click(Field<SettingsSaveBar>(window, "_saveBar"), "_cancel");
            Assert.Equal(initial.Normalize(), Current(window).Normalize());
            Assert.Same(notificationItem, navigation.SelectedItem);

            window.ApplyAdjustmentResult(initial with { NotificationHud = initial.NotificationHud with { Width = 200 } }, AdjustmentKind.NotificationWidth);
            navigation.SelectedItem = generalItem;
            Click(Field<SettingsSaveBar>(window, "_saveBar"), "_save");
            Assert.Null(saved);
            Assert.Equal(SettingsStatus.Error, Field<SettingsEditState>(window, "_editState").Status);
            Assert.Equal(200, Current(window).NotificationHud.Width);
            Assert.Equal(initial.Normalize(), Field<SettingsEditState>(window, "_editState").SavedSettings);
            failSave = false;
            Click(Field<SettingsSaveBar>(window, "_saveBar"), "_save");
            Assert.Equal(initial with { NotificationHud = initial.NotificationHud with { Width = 200 } }, saved);
            navigation.SelectedItem = notificationItem;
            window.ApplyAdjustmentResult(initial with { NotificationHud = initial.NotificationHud with { Width = 250 } }, AdjustmentKind.NotificationWidth);
        }
        finally { window.Close(); }
        Assert.Equal(2, saveCalls);
        var reopened = new SettingsWindow(saved!, _ => true);
        try { Assert.Equal(saved, Current(reopened).Normalize()); }
        finally { reopened.Close(); }
    }

    private static void VerifyLocalizedLayout(SettingsWindow window, NavigationView navigation, NavigationViewItem item)
    {
        var output = Environment.GetEnvironmentVariable("GHOST_NOTIFICATION_OUTPUT");
        foreach (var language in new[] { UiLanguage.Chinese, UiLanguage.English })
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        foreach (var expanded in new[] { false, true })
        {
            Field<ComboBox>(window, "_language").SelectedIndex = language == UiLanguage.Chinese ? 0 : 1;
            window.RequestedThemeVariant = theme;
            navigation.IsPaneOpen = expanded;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(UiText.Text(language, "notificationsPage"), item.Content);
            Assert.Equal(item.Content, Field<TextBlock>(window, "_pageTitle").Text);
            Assert.Equal(UiText.Text(language, "notificationsPageDescription"), Field<TextBlock>(window, "_pageDescription").Text);
            Assert.True(Field<ContentControl>(window, "_pageHost").Bounds.Width > 500);
            if (string.IsNullOrEmpty(output)) continue;
            // Pump the native dispatcher so navigation transitions finish before screenshot capture.
            using (var settled = new CancellationTokenSource(TimeSpan.FromMilliseconds(350)))
            {
                try { Dispatcher.UIThread.MainLoop(settled.Token); }
                catch (OperationCanceledException) { }
            }
            Directory.CreateDirectory(output);
            using var bitmap = new RenderTargetBitmap(new Avalonia.PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
            bitmap.Render(window);
            bitmap.Save(Path.Combine(output, $"notifications-{language}-{theme}-{(expanded ? "expanded" : "compact")}.png"));
        }
    }

    private static void Click(object owner, string field) => Field<Button>(owner, field).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static AppSettings Current(SettingsWindow window) => (AppSettings)typeof(SettingsWindow)
        .GetMethod("GetSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
}
