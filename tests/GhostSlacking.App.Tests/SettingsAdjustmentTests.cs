using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
        Assert.Equal(index, SettingAdjustment.FrameIndex(value));
        Assert.Equal(value, SettingAdjustment.FrameValue(index));
    }

    [Theory]
    [InlineData(144, 16, 1, 160)]
    [InlineData(144, 16, -1, 128)]
    [InlineData(64, 24, -1, 64)]
    [InlineData(790, 24, 1, 800)]
    public void Step_preview_overlays_one_step_without_changing_the_configured_diameter(int diameter, int step, int direction, int expected)
    {
        var initial = new AppSettings { RevealDiameterPx = diameter };
        var current = initial with { RevealDiameterStepPx = step };
        var comparison = RevealPreviewComparison.Create(initial, current, AdjustmentKind.DiameterStep, direction);
        Assert.Equal(diameter, comparison.BeforeDiameter);
        Assert.Equal(expected, comparison.AfterDiameter);
        Assert.Equal(diameter, current.RevealDiameterPx);
        Assert.True(comparison.IsStep);
        Assert.True(comparison.ReferenceExtent > Math.Max(diameter, expected));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(128)]
    public void Feather_comparison_keeps_the_hard_core_and_adds_the_selected_outer_width(int feather)
    {
        var initial = new AppSettings();
        var current = initial with { RevealSoftEdgeWidthPx = feather };
        var comparison = RevealPreviewComparison.Create(initial, current, AdjustmentKind.SoftEdgeWidth);
        Assert.Equal(144, comparison.BeforeDiameter);
        Assert.Equal(144, comparison.AfterDiameter);
        Assert.Equal(feather, comparison.FeatherWidth);
        Assert.True(comparison.IsFeather);
        Assert.Equal(144 + 2 * feather + 48, comparison.ReferenceExtent);
    }

    [Fact]
    public void Applying_an_item_preserves_every_other_setting_and_rejects_non_finite_values()
    {
        var initial = new AppSettings { RevealDiameterPx = 240, Language = UiLanguage.English };
        foreach (var kind in Enum.GetValues<AdjustmentKind>())
        {
            var spec = SettingAdjustment.For(kind);
            var result = SettingAdjustment.Apply(initial, kind, spec.Maximum);
            Assert.Equal(spec.Maximum, SettingAdjustment.Read(result, kind));
            foreach (var other in Enum.GetValues<AdjustmentKind>().Where(k => k != kind))
                Assert.Equal(SettingAdjustment.Read(initial, other), SettingAdjustment.Read(result, other));
            Assert.Equal(initial.Language, result.Language);
            Assert.Equal(initial, SettingAdjustment.Apply(initial, kind, double.NaN));
            Assert.Equal(initial, SettingAdjustment.Apply(initial, kind, double.PositiveInfinity));
        }
    }

    // Reuse the solution's single Avalonia STA fixture.
    internal static void VerifyUi()
    {
        VerifySliderSynchronization();
        var owner = new SettingsWindow(new AppSettings(), _ => true) { ShowActivated = false };
        owner.Show();
        try
        {
            VerifyDraftLifecycle(owner);
            VerifyNotificationContentParity(owner);
            VerifyAnimationLifecycle(owner);
            VerifyBlurContrast(owner);
            VerifyLocalizedLayouts(owner);
        }
        finally { owner.Close(); }
    }

    private static void VerifySliderSynchronization()
    {
        var editor = new SliderNumberEditor("Diameter", "px", 144, 64, 800, 8, 144, "Reset");
        editor.Slider.Value = 232;
        Assert.Equal(232, editor.Number.Value);
        editor.Number.Value = 317;
        Assert.Equal(317, editor.Slider.Value);
        editor.Number.Value = double.NaN;
        Assert.Equal(317, editor.Number.Value);
        editor.Value = double.PositiveInfinity;
        Assert.Equal(317, editor.Value);
        editor.Value = 999;
        Assert.Equal(800, editor.Number.Value);
        editor.Value = -1;
        Assert.Equal(64, editor.Slider.Value);
        editor.ResetButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(144, editor.Value);
    }

    private static void VerifyDraftLifecycle(SettingsWindow owner)
    {
        var results = new List<AppSettings>();
        var initial = new AppSettings { RevealDiameterPx = 240, NotificationHud = new NotificationHudSettings { Width = 190 } };
        using (var discarded = new SettingsAdjustmentFlyout(initial, AdjustmentKind.Diameter, results.Add))
        {
            discarded.OpenAt(owner);
            discarded.NumberEditor!.Value = 400;
            discarded.Hide();
            discarded.Complete();
            Assert.Empty(results);
        }
        using (var escaped = new SettingsAdjustmentFlyout(initial, AdjustmentKind.NotificationWidth, results.Add))
        {
            escaped.OpenAt(owner);
            escaped.NumberEditor!.Number.Focus();
            escaped.NumberEditor.Number.Text = "250";
            escaped.NumberEditor.Number.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.False(escaped.IsOpen);
            Assert.Empty(results);
        }
        using (var accepted = new SettingsAdjustmentFlyout(initial, AdjustmentKind.Diameter, results.Add))
        {
            accepted.OpenAt(owner);
            accepted.View.UpdateLayout();
            var slider = accepted.NumberEditor!.Slider;
            slider.GetVisualDescendants().OfType<Track>().Single().Value = 280;
            Assert.Equal(280, accepted.NumberEditor.Value);
            slider.Value = 240;
            accepted.View.UpdateLayout();
            var track = slider.GetVisualDescendants().OfType<Track>().Single();
            var thumb = track.Thumb!;
            var center = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), slider)!.Value;
            var expectedX = thumb.Bounds.Width / 2 + (slider.Bounds.Width - thumb.Bounds.Width) * (240 - slider.Minimum) / (slider.Maximum - slider.Minimum);
            Assert.InRange(center.X, expectedX - 2, expectedX + 2);
            slider.Focus();
            slider.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
            Assert.Equal(248, accepted.NumberEditor.Value);
            accepted.NumberEditor!.Number.Focus();
            accepted.NumberEditor.Number.Text = "320";
            accepted.Complete();
            Assert.Equal(initial with { RevealDiameterPx = 320 }, Assert.Single(results));
        }
        owner.ApplyAdjustmentResult(results[0] with { NotificationHud = new NotificationHudSettings { Width = 300 } }, AdjustmentKind.Diameter);
        Assert.Equal(320, Current(owner).RevealDiameterPx);
        Assert.Equal(132, Current(owner).NotificationHud.Width);
        using (var step = new SettingsAdjustmentFlyout(initial, AdjustmentKind.DiameterStep, _ => { }))
        {
            step.OpenAt(owner);
            step.NumberEditor!.Value = 24;
            Assert.Equal(264, step.RevealSample!.Comparison.AfterDiameter);
            step.DemonstrateStep(-1);
            Assert.Equal(216, step.RevealSample.Comparison.AfterDiameter);
            Assert.Equal(240, step.Draft.RevealDiameterPx);
            step.Hide();
        }
        owner.OpenAdjustment(AdjustmentKind.Diameter, owner);
        var first = owner.ActiveAdjustment!;
        owner.OpenAdjustment(AdjustmentKind.SoftEdgeWidth, owner);
        Assert.False(first.IsOpen);
        var second = owner.ActiveAdjustment!;
        Assert.Equal(AdjustmentKind.SoftEdgeWidth, second.Kind);
        owner.OpenAdjustment(AdjustmentKind.SoftEdgeWidth, owner);
        Assert.Null(owner.ActiveAdjustment);
        typeof(SettingsWindow).GetMethod("CancelSettingsEdits", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(owner, null);
        Assert.Equal(144, Current(owner).RevealDiameterPx);
    }

    private static void VerifyNotificationContentParity(SettingsWindow owner)
    {
        using var flyout = new SettingsAdjustmentFlyout(new AppSettings(), AdjustmentKind.NotificationWidth, _ => { });
        flyout.OpenAt(owner);
        var runtime = new NotificationWindow(preview: true);
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var width in new[] { 100d, 132, 300 })
            {
                owner.RequestedThemeVariant = theme;
                runtime.RequestedThemeVariant = theme;
                flyout.NumberEditor!.Value = width;
                foreach (var choice in new[] { 0, 1 })
                {
                    flyout.MessageChoice!.SelectedIndex = choice;
                    var hud = flyout.Draft.NotificationHud;
                    runtime.Update(flyout.NotificationSample!.Message.Text!, UserNotificationSeverity.Info, false);
                    runtime.Configure(hud);
                    Assert.Equal(runtime.Hud.Surface.Width, flyout.NotificationSample.Surface.Width);
                    Assert.Equal(runtime.Hud.Message.FontSize, flyout.NotificationSample.Message.FontSize);
                }
            }
        }
        finally { flyout.Hide(); runtime.Close(); }
    }

    private static void VerifyAnimationLifecycle(SettingsWindow owner)
    {
        using var flyout = new SettingsAdjustmentFlyout(new AppSettings(), AdjustmentKind.FrameRate, _ => { }, new FixedDisplay());
        flyout.OpenAt(owner);
        Assert.False(flyout.IsAnimationRunning);
        flyout.FrameRateSlider!.Value = 2;
        Assert.Equal(75, flyout.Decision.TargetFramesPerSecond);
        flyout.SetPlaying(true);
        Assert.True(flyout.IsAnimationRunning);
        flyout.SetPlaying(false);
        Assert.False(flyout.IsAnimationRunning);
        flyout.SetPlaying(true);
        owner.WindowState = WindowState.Minimized;
        Assert.False(flyout.IsAnimationRunning);
        Assert.False(flyout.IsOpen);
        owner.WindowState = WindowState.Normal;
    }

    private static void VerifyLocalizedLayouts(SettingsWindow owner)
    {
        var directory = Environment.GetEnvironmentVariable("GHOST_ADJUSTMENT_OUTPUT");
        foreach (var language in new[] { UiLanguage.Chinese, UiLanguage.English })
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        foreach (var kind in Enum.GetValues<AdjustmentKind>())
        {
            owner.RequestedThemeVariant = theme;
            using var flyout = new SettingsAdjustmentFlyout(new AppSettings { Language = language }, kind, _ => { }, new FixedDisplay());
            flyout.OpenAt(owner);
            flyout.View.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.InRange(flyout.View.Bounds.Width, 220, 340);
            Assert.True(flyout.CompleteButton.Bounds.Width > 30);
            if (!string.IsNullOrEmpty(directory) && kind is AdjustmentKind.DiameterStep or AdjustmentKind.SoftEdgeWidth or AdjustmentKind.NotificationWidth or AdjustmentKind.FrameRate or AdjustmentKind.BlurLevel)
            {
                Directory.CreateDirectory(directory);
                var view = flyout.Presenter!;
                foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
                {
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)(view.Bounds.Width * scale), (int)(view.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
                    bitmap.Render(view);
                    bitmap.Save(System.IO.Path.Combine(directory, $"{kind}-{language}-{theme}-{scale}.png"));
                }
            }
            flyout.Hide();
        }
        Assert.Equal("GhostSlacking", UiText.Text(UiLanguage.Chinese, "title"));
        Assert.Equal("GhostSlacking", UiText.Text(UiLanguage.English, "title"));
    }

    private static void VerifyBlurContrast(SettingsWindow owner)
    {
        owner.RequestedThemeVariant = ThemeVariant.Light;
        using var flyout = new SettingsAdjustmentFlyout(new AppSettings(), AdjustmentKind.BlurLevel, _ => { });
        flyout.OpenAt(owner);
        var contrasts = new List<long>();
        var references = new List<long>();
        var directory = Environment.GetEnvironmentVariable("GHOST_ADJUSTMENT_OUTPUT");
        for (var level = 0; level < 3; level++)
        {
            flyout.SelectChoice(level);
            flyout.View.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var sample = flyout.RevealSample!;
            Assert.True(sample.IsBlurComparison);
            using var image = new RenderTargetBitmap(new PixelSize((int)sample.Bounds.Width, (int)sample.Bounds.Height), new Vector(96, 96));
            image.Render(sample);
            using var stream = new MemoryStream();
            image.Save(stream);
            stream.Position = 0;
            using var pixels = SkiaSharp.SKBitmap.Decode(stream);
            long Contrast(int start)
            {
                long total = 0;
                for (var x = start; x < start + 70; x++)
                for (var y = 85; y < 158; y++)
                {
                    var current = pixels.GetPixel(x, y);
                    var next = pixels.GetPixel(x, y + 1);
                    total += Math.Abs(current.Red - next.Red) + Math.Abs(current.Green - next.Green) + Math.Abs(current.Blue - next.Blue);
                }
                return total;
            }
            references.Add(Contrast(47));
            contrasts.Add(Contrast(216));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                var view = flyout.Presenter!;
                using var screenshot = new RenderTargetBitmap(new PixelSize((int)view.Bounds.Width, (int)view.Bounds.Height), new Vector(96, 96));
                screenshot.Render(view);
                screenshot.Save(System.IO.Path.Combine(directory, $"BlurLevel-contrast-{level}.png"));
            }
        }
        Assert.Equal(references[0], references[1]);
        Assert.Equal(references[0], references[2]);
        Assert.True(contrasts[0] > contrasts[1] * 1.15 && contrasts[1] > contrasts[2] * 1.15,
            $"Blurred edge contrast must fall visibly with each level: {string.Join(", ", contrasts)}");
        flyout.Hide();
        using var inactive = new SettingsAdjustmentFlyout(new AppSettings { RevealSoftEdgeWidthPx = 0 }, AdjustmentKind.BlurLevel, _ => { });
        inactive.OpenAt(owner);
        inactive.SelectChoice(2);
        Assert.Equal(RevealBlurLevel.High, inactive.Draft.RevealBlurLevel);
        Assert.Equal(0, inactive.Draft.RevealSoftEdgeWidthPx);
        Assert.Contains(inactive.View.GetLogicalDescendants().OfType<TextBlock>(),
            text => text.Text?.Contains(UiText.Text(UiLanguage.Chinese, "adjustBlurInactive"), StringComparison.Ordinal) == true);
        inactive.Hide();
    }

    private static AppSettings Current(SettingsWindow window) => (AppSettings)typeof(SettingsWindow)
        .GetMethod("GetSettings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null)!;
    private sealed class FixedDisplay : IDisplayRefreshRateProvider
    {
        public DisplayRefreshRateInfo? GetForPoint(System.Drawing.Point point) => new("test", 75);
        public void Invalidate() { }
    }
}
