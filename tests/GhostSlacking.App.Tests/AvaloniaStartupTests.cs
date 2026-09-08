using GhostSlacking.App;
using Avalonia.Controls;
using System.Drawing;
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
                using var trayIcon = new TrayIcon
                {
                    Icon = AppIcon.Instance,
                    IsVisible = true,
                    ToolTipText = "GhostSlacking test"
                };
                using var notifications = new AvaloniaNotificationService(() => Point.Empty, NullLogger.Instance);
                notifications.Show("ready", UserNotificationSeverity.Info);
                trayIcon.IsVisible = false;
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
}
