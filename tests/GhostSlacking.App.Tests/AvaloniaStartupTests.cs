using GhostSlacking.App;
using Avalonia.Controls;
using Avalonia.Layout;
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
                using var trayIcon = new TrayIcon
                {
                    Icon = AppIcon.Instance,
                    IsVisible = true,
                    ToolTipText = "GhostSlacking test"
                };
                using var notifications = new AvaloniaNotificationService(() => Point.Empty, NullLogger.Instance);
                notifications.Show("ready", UserNotificationSeverity.Info);
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

    private static void AssertDefaultSettingsWindowLayout(SettingsWindow settingsWindow)
    {
        var navigation = GetPrivateField<NavigationView>(settingsWindow, "_navigation");
        var infoBar = GetPrivateField<InfoBar>(settingsWindow, "_infoBar");
        var footerContent = Assert.IsType<Grid>(infoBar.Parent);
        var footer = Assert.IsType<Border>(footerContent.Parent);
        var content = Assert.IsAssignableFrom<Control>(settingsWindow.Content);

        Assert.Equal(settingsWindow.MinWidth, settingsWindow.Width);
        Assert.Equal(settingsWindow.MinHeight, settingsWindow.Height);
        Assert.False(navigation.IsPaneOpen);
        Assert.Equal(32, infoBar.Height);
        Assert.Equal(HorizontalAlignment.Left, infoBar.HorizontalAlignment);

        content.Measure(new Avalonia.Size(settingsWindow.Width, settingsWindow.Height));
        var closedHeight = footer.DesiredSize.Height;
        infoBar.IsOpen = true;
        content.Measure(new Avalonia.Size(settingsWindow.Width, settingsWindow.Height));
        Assert.Equal(closedHeight, footer.DesiredSize.Height);
        infoBar.IsOpen = false;
    }

    private static T GetPrivateField<T>(object instance, string name) where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsType<T>(field?.GetValue(instance));
    }
}
