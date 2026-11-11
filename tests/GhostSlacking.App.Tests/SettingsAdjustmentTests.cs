using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using GhostSlacking.App;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App.Tests;

public sealed class SettingsAdjustmentTests
{
    [Theory]
    [InlineData(PeekFrameRateLimit.Auto, 0)]
    [InlineData(PeekFrameRateLimit.Fps60, 1)]
    [InlineData(PeekFrameRateLimit.Fps90, 2)]
    [InlineData(PeekFrameRateLimit.Fps120, 3)]
    public void Frame_rate_ticks_round_trip_without_changing_the_settings_contract(PeekFrameRateLimit value, int index)
    {
        Assert.Equal(index, RevealAdjustmentWindow.FrameIndex(value));
        Assert.Equal(value, RevealAdjustmentWindow.FrameValue(index));
    }

    // Called on the existing, single Avalonia STA platform fixture.
    internal static void VerifyUi()
    {
        VerifySliderSynchronization();
        VerifyModalDraftLifecycle();
        VerifyNotificationContentParity();
        VerifyAnimationLifecycle();
        VerifyLocalizedLayouts();
    }

    private static void VerifySliderSynchronization()
    {
        var editor = new SliderNumberEditor("Diameter", "px", 144, 64, 800, 8, 144, "Reset");
        editor.Slider.Value = 232;
        Assert.Equal(232, editor.Number.Value);
        editor.Number.Value = 317;
        Assert.Equal(317, editor.Slider.Value);
        editor.Value = double.NaN;
        Assert.Equal(317, editor.Value);
        editor.Number.Value = double.NaN;
        Assert.Equal(317, editor.Number.Value);
        editor.Value = double.PositiveInfinity;
        Assert.Equal(317, editor.Value);
        editor.Value = 999;
        Assert.Equal(800, editor.Number.Value);
        editor.Value = -1;
        Assert.Equal(64, editor.Slider.Value);
        editor.GetLogicalDescendants().OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(144, editor.Value);
    }

    private static void VerifyModalDraftLifecycle()
    {
        var saves = new List<AppSettings>();
        var saveSucceeds = false;
        var initial = new AppSettings { RevealDiameterPx = 200, NotificationHud = new NotificationHudSettings { Width = 190 } };
        var owner = new SettingsWindow(initial, s => { if (!saveSucceeds) return false; saves.Add(s); return true; });
        owner.ShowActivated = false;
        owner.Show();
        var draft = initial with { RevealDiameterPx = 240 };
        var discarded = new RevealAdjustmentWindow(draft, new FixedDisplay()) { ShowActivated = false };
        var cancelled = discarded.ShowDialog<AppSettings?>(owner);
        discarded.Diameter.Value = 400;
        discarded.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(cancelled.GetAwaiter().GetResult());
        Assert.Null(discarded.AcceptedSettings);
        Assert.Empty(saves);

        var reveal = new RevealAdjustmentWindow(draft, new FixedDisplay()) { ShowActivated = false };
        var finished = reveal.ShowDialog<AppSettings?>(owner);
        Assert.Equal(240, reveal.Diameter.Value);
        reveal.DiameterStep.Value = 24;
        reveal.DemonstrateStep(1);
        Assert.Equal(264, reveal.DemoDiameter);
        Assert.Equal(240, reveal.Diameter.Value);
        reveal.Diameter.Value = 320;
        reveal.FrameRate.Value = 2;
        Assert.Equal(75, reveal.Decision.TargetFramesPerSecond);
        reveal.Complete();
        Dispatcher.UIThread.RunJobs();
        var result = finished.GetAwaiter().GetResult();
        Assert.NotNull(result);
        // Only fields belonging to the accepted group may be written back.
        owner.ApplyAdjustmentResult(result with { NotificationHud = new NotificationHudSettings { Width = 300 } }, notification: false);
        Assert.Equal(190, Current(owner).NotificationHud.Width);
        Assert.Equal(320, Current(owner).RevealDiameterPx);
        Assert.Empty(saves);
        InvokeSave(owner);
        Assert.Empty(saves);
        Assert.Equal(320, Current(owner).RevealDiameterPx);
        saveSucceeds = true;
        InvokeSave(owner);
        Assert.Equal(PeekFrameRateLimit.Fps90, Assert.Single(saves).PeekFrameRateLimit);

        var notification = new NotificationAdjustmentWindow(Current(owner)) { ShowActivated = false };
        var notificationResult = notification.ShowDialog<AppSettings?>(owner);
        notification.MaximumWidth.Value = 250;
        notification.HorizontalPosition.Value = 0;
        notification.Complete();
        Dispatcher.UIThread.RunJobs();
        owner.ApplyAdjustmentResult(notificationResult.GetAwaiter().GetResult()! with { RevealDiameterPx = 800 }, notification: true);
        Assert.Equal(320, Current(owner).RevealDiameterPx);
        Assert.Equal(250, Current(owner).NotificationHud.Width);
        typeof(SettingsWindow).GetMethod("CancelSettingsEdits", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(owner, null);
        Assert.Equal(190, Current(owner).NotificationHud.Width);
        Assert.Equal(320, Current(owner).RevealDiameterPx);
        owner.Close();
    }

    private static void VerifyNotificationContentParity()
    {
        var dialog = new NotificationAdjustmentWindow(new AppSettings());
        var runtime = new NotificationWindow(preview: true);
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        foreach (var width in new[] { 100d, 132, 300 })
        foreach (var height in new[] { 32d, 44, 96 })
        {
            dialog.RequestedThemeVariant = theme;
            runtime.RequestedThemeVariant = theme;
            dialog.MaximumWidth.Value = width;
            dialog.HudHeight.Value = height;
            foreach (var choice in new[] { 0, 1 })
            {
                dialog.MessageChoice.SelectedIndex = choice;
                var hud = new NotificationHudSettings { Width = width, Height = height };
                runtime.Update(dialog.Sample.Message.Text!, UserNotificationSeverity.Info, false);
                runtime.Configure(hud);
                Assert.Equal(runtime.Hud.Surface.Width, dialog.Sample.Surface.Width);
                Assert.Equal(runtime.Hud.Surface.Height, dialog.Sample.Surface.Height);
                Assert.Equal(runtime.Hud.Message.FontSize, dialog.Sample.Message.FontSize);
            }
        }
        dialog.Close();
        runtime.Close();
    }

    private static void VerifyAnimationLifecycle()
    {
        var dialog = new RevealAdjustmentWindow(new AppSettings(), new FixedDisplay()) { ShowActivated = false };
        Assert.False(dialog.IsAnimationRunning);
        dialog.Show();
        Assert.True(dialog.IsAnimationRunning);
        dialog.SetPlaying(false);
        Assert.False(dialog.IsAnimationRunning);
        dialog.SetPlaying(true);
        Assert.True(dialog.IsAnimationRunning);
        dialog.WindowState = WindowState.Minimized;
        Assert.False(dialog.IsAnimationRunning);
        dialog.WindowState = WindowState.Normal;
        Assert.True(dialog.IsAnimationRunning);
        dialog.Close();
        Assert.False(dialog.IsAnimationRunning);
    }

    private static void VerifyLocalizedLayouts()
    {
        var directory = Environment.GetEnvironmentVariable("GHOST_ADJUSTMENT_OUTPUT");
        foreach (var language in new[] { UiLanguage.Chinese, UiLanguage.English })
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        foreach (var notification in new[] { false, true })
        {
            var settings = new AppSettings { Language = language };
            SettingsAdjustmentWindow dialog = notification ? new NotificationAdjustmentWindow(settings) : new RevealAdjustmentWindow(settings, new FixedDisplay());
            dialog.RequestedThemeVariant = theme;
            dialog.ShowActivated = false;
            dialog.Show();
            dialog.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(UiText.Text(language, notification ? "adjustNotificationTitle" : "adjustRevealTitle"), dialog.Title);
            Assert.True(dialog.CompleteButton.Bounds.Width >= 96);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
                {
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)(dialog.Bounds.Width * scale), (int)(dialog.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
                    bitmap.Render(dialog);
                    bitmap.Save(System.IO.Path.Combine(directory, $"{(notification ? "notification" : "reveal")}-{language}-{theme}-{scale}.png"));
                }
            }
            dialog.Close();
        }
        Assert.Equal("GhostSlacking", UiText.Text(UiLanguage.Chinese, "title"));
        Assert.Equal("GhostSlacking", UiText.Text(UiLanguage.English, "title"));
    }

    private static AppSettings Current(SettingsWindow window) => (AppSettings)typeof(SettingsWindow)
        .GetMethod("GetSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null)!;
    private static void InvokeSave(SettingsWindow window) => typeof(SettingsWindow)
        .GetMethod("OnSaveClicked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, [null, new RoutedEventArgs()]);

    private sealed class FixedDisplay : IDisplayRefreshRateProvider
    {
        public DisplayRefreshRateInfo? GetForPoint(System.Drawing.Point point) => new("test", 75);
        public void Invalidate() { }
    }
}
