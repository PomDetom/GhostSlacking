using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using GhostSlacking.Setup;

namespace GhostSlacking.App.Tests;

public sealed class InstallerWindowTests
{
    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    public void Rendered_icon_has_transparent_padding_white_eyes_and_an_unfilled_head_until_success(int dpi)
    {
        OnStaThread(() =>
        {
            var window = new InstallerWindow(@"C:\Apps\GhostSlacking") { ShowActivated = false };
            try
            {
                var previousGreen = -1;
                foreach (var progress in new[] { 0, 1, 50, 99, 100 })
                {
                    window.SetProgress(progress, "测试进度");
                    var bitmap = window.RenderPreview(dpi);
                    var counts = CountPixels(bitmap);
                    Assert.Equal(0, CornerAlpha(bitmap));
                    Assert.True(counts.White > 0, "The white eyes must remain visible.");
                    Assert.True(counts.Green >= previousGreen, "Real progress must fill upward.");
                    if (progress == 0) Assert.Equal(0, counts.Green);
                    else Assert.True(counts.Green > 0);
                    if (progress < 100) Assert.True(counts.Gray > 0, "99% must still have a gray part.");
                    else Assert.Equal(0, counts.Gray);
                    previousGreen = counts.Green;
                }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Hiding_options_preserves_icon_screen_position_and_keeps_only_the_artwork()
    {
        OnStaThread(() =>
        {
            var window = new InstallerWindow(@"C:\Apps\GhostSlacking") { ShowActivated = false };
            try
            {
                window.ConfigureReady("安装", freshInstall: true, manual: true, uninstall: false);
                window.FreshInstallOptions.IsExpanded = true;
                window.RenderPreview();
                var position = window.IconButton.PointToScreen(new Point());
                var oldHeight = window.ActualHeight;
                window.BeginInstall();
                Assert.Equal(position, window.IconButton.PointToScreen(new Point()));
                Assert.True(window.ActualHeight < oldHeight);
                Assert.Equal(Visibility.Collapsed, window.OptionsArea.Visibility);
                Assert.Equal(0, CornerAlpha(window.RenderPreview()));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Repeated_icon_activation_cannot_start_installation_or_launch_the_application_twice()
    {
        OnStaThread(() =>
        {
            var window = new InstallerWindow(@"C:\Apps\GhostSlacking");
            var starts = 0;
            var finishes = 0;
            window.StartRequested += (_, _) => { starts++; window.BeginInstall(); };
            window.FinishRequested += (_, _) => finishes++;
            window.ConfigureReady("更新", freshInstall: false, manual: true, uninstall: false);
            window.IconButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.IconButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, starts);
            window.EnableCompletion(succeeded: true);
            window.IconButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.IconButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, finishes);
            window.Close();
        });
    }

    [Theory]
    [InlineData("relative")]
    [InlineData(@"\\server\share\GhostSlacking")]
    [InlineData(@"C:GhostSlacking")]
    public void Invalid_directory_is_rejected_before_the_installation_can_start(string directory)
    {
        var selection = new InstallerSelection(directory, true, false, true);
        Assert.Throws<ArgumentException>(() => selection.ValidateDirectory());
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void Keyboard_activation_starts_the_icon_and_escape_requests_safe_cancellation(Key key)
    {
        OnStaThread(() =>
        {
            var window = new InstallerWindow(@"C:\Apps\GhostSlacking") { ShowActivated = false };
            try
            {
                var starts = 0;
                var cancellations = 0;
                window.StartRequested += (_, _) => { starts++; window.BeginInstall(); };
                window.CancelRequested += (_, _) => cancellations++;
                window.ConfigureReady("安装", freshInstall: true, manual: true, uninstall: false);
                window.RenderPreview();
                var source = PresentationSource.FromVisual(window)!;
                if (key == Key.Enter)
                    window.IconButton.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                else
                {
                    window.IconButton.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
                    { RoutedEvent = Keyboard.KeyDownEvent });
                    window.IconButton.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 1, key)
                    { RoutedEvent = Keyboard.KeyUpEvent });
                }
                Assert.Equal(1, starts);
                window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 2, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Assert.Equal(1, cancellations);
                Assert.True(window.IsVisible);
            }
            finally { window.Close(); }
        });
    }

    private static (int Gray, int Green, int White) CountPixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var gray = 0; var green = 0; var white = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] != 255) continue;
            if (pixels[i] == 0x40 && pixels[i + 1] == 0x40 && pixels[i + 2] == 0x40) gray++;
            if (pixels[i] == 0xA5 && pixels[i + 1] == 0xB2 && pixels[i + 2] == 0x1C) green++;
            if (pixels[i] == 255 && pixels[i + 1] == 255 && pixels[i + 2] == 255) white++;
        }
        return (gray, green, white);
    }

    private static byte CornerAlpha(BitmapSource bitmap)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        return pixel[3];
    }

    private static void OnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
