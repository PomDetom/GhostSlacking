using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Raw;
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
            VerifyPointerInteraction();
            VerifyNotificationContentParity(owner);
            VerifyAnimationLifecycle(owner);
            VerifyFrameRateDescriptions(owner);
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
        Assert.False(flyout.IsAnimationRunning);
        flyout.OpenAt(owner);
        Assert.True(flyout.IsAnimationRunning);
        flyout.FrameRateSlider!.Value = 2;
        Assert.Equal(75, flyout.Decision.TargetFramesPerSecond);
        var frame = new DispatcherFrame();
        DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(60));
        Dispatcher.UIThread.PushFrame(frame);
        Assert.True(flyout.RevealSample!.AnimationSeconds > 0);
        owner.WindowState = WindowState.Minimized;
        Assert.False(flyout.IsAnimationRunning);
        Assert.False(flyout.IsOpen);
        owner.WindowState = WindowState.Normal;

        foreach (var close in new[] { "hide", "escape", "complete", "owner" })
        {
            var host = new Window { Width = 500, Height = 650, ShowActivated = false };
            host.Show();
            using var animated = new SettingsAdjustmentFlyout(new AppSettings(), AdjustmentKind.FrameRate, _ => { }, new FixedDisplay());
            animated.OpenAt(host);
            Assert.True(animated.IsAnimationRunning);
            switch (close)
            {
                case "hide": animated.Hide(); break;
                case "escape": animated.View.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); break;
                case "complete": animated.Complete(); break;
                case "owner": host.Close(); break;
            }
            Assert.False(animated.IsAnimationRunning);
            Assert.False(animated.IsOpen);
            host.Close();
        }
        using var still = new SettingsAdjustmentFlyout(new AppSettings(), AdjustmentKind.Diameter, _ => { });
        still.OpenAt(owner);
        Assert.False(still.IsAnimationRunning);
        still.Hide();
    }

    private static void VerifyPointerInteraction()
    {
        var anchor = new Button { Content = "Slider input probe", Width = 200, Height = 32, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var owner = new Window { Content = anchor, Width = 500, Height = 750, ShowActivated = false };
        owner.Show();
        PumpUi(TimeSpan.FromMilliseconds(180));
        try
        {
            foreach (var kind in Enum.GetValues<AdjustmentKind>().Where(k => k is not (AdjustmentKind.BlurLevel or AdjustmentKind.Shape)))
            {
                using var flyout = new SettingsAdjustmentFlyout(new AppSettings(), kind, _ => { }, new FixedDisplay());
                flyout.OpenAt(anchor);
                flyout.View.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var slider = flyout.FrameRateSlider ?? flyout.NumberEditor!.Slider;
                var track = slider.GetVisualDescendants().OfType<Track>().Single();
                var left = slider.GetVisualDescendants().OfType<RepeatButton>().Single(b => b.Name == "StepDecreaseEnd");
                var right = slider.GetVisualDescendants().OfType<RepeatButton>().Single(b => b.Name == "StepIncreaseEnd");
                var top = TopLevel.GetTopLevel(slider)!;
                PumpUi(TimeSpan.FromMilliseconds(180));
                // Raw-input constructors are omitted from Avalonia's reference assembly. Drive its
                // runtime input manager so hit testing, capture, hover and repeat buttons all run.
                using var mouse = (MouseDevice)Activator.CreateInstance(typeof(MouseDevice), new object?[] { null })!;
                var managerType = typeof(InputElement).Assembly.GetType("Avalonia.Input.InputManager")!;
                var manager = managerType.GetProperty("Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
                ulong timestamp = 0;
                void Send(RawPointerEventType type, double fraction, bool pressed, double y = 16, double? trackX = null, Control? expectedHit = null)
                {
                    flyout.View.UpdateLayout();
                    if (type == RawPointerEventType.LeftButtonDown) PumpUi(TimeSpan.FromMilliseconds(100));
                    var x = trackX ?? track.Thumb!.Bounds.Width / 2 + (track.Bounds.Width - track.Thumb.Bounds.Width) * fraction;
                    var point = track.TranslatePoint(new Point(x, y), top)!.Value;
                    var hit = top.InputHitTest(point);
                    if (type == RawPointerEventType.LeftButtonDown)
                    {
                        bool Ready() => hit is Visual visual && visual.GetSelfAndVisualAncestors().Contains(expectedHit ?? slider);
                        for (var attempt = 0; !Ready() && attempt < 20; attempt++)
                        {
                            PumpUi(TimeSpan.FromMilliseconds(25));
                            flyout.View.UpdateLayout();
                            point = track.TranslatePoint(new Point(x, y), top)!.Value;
                            hit = top.InputHitTest(point);
                        }
                        Assert.True(Ready(), $"{kind} press must hit {expectedHit?.Name ?? "slider"}; hit {hit}, point {point}.");
                    }
                    var input = Activator.CreateInstance(typeof(RawPointerEventArgs), mouse, ++timestamp, (IInputRoot)top, type, point,
                        pressed ? RawInputModifiers.LeftMouseButton : RawInputModifiers.None)!;
                    typeof(RawPointerEventArgs).GetProperty("InputHitTestResult", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .SetValue(input, (hit, hit));
                    managerType.GetMethod("ProcessInput")!.Invoke(manager, [input]);
                    flyout.View.UpdateLayout();
                }
                double Expected(double fraction)
                {
                    var value = slider.Minimum + (slider.Maximum - slider.Minimum) * Math.Clamp(fraction, 0, 1);
                    return kind == AdjustmentKind.FrameRate ? Math.Floor(value + 0.5) : Math.Round(value);
                }
                void AssertSelected(double expected)
                {
                    Assert.Equal(expected, slider.Value, 5);
                    Assert.Equal(expected, SettingAdjustment.Read(flyout.Draft, kind), 5);
                    if (flyout.NumberEditor is { } editor) Assert.Equal(expected, editor.Number.Value, 5);
                }
                // Only the two short ends use the original LargeChange click handlers.
                foreach (var (button, direction, x) in new[] { (left, -1, 2d), (right, 1, track.Bounds.Width - 2) })
                {
                    Assert.Equal(32, button.Bounds.Width);
                    slider.Value = Expected(0.5);
                    for (var click = 0; click < 2; click++)
                    {
                        var expected = Math.Clamp(slider.Value + direction * slider.LargeChange, slider.Minimum, slider.Maximum);
                        Send(RawPointerEventType.LeftButtonDown, 0, true, trackX: x, expectedHit: button);
                        Send(RawPointerEventType.LeftButtonUp, 0, false, trackX: x);
                        AssertSelected(expected);
                    }
                    slider.Value = direction < 0 ? slider.Minimum + 1 : slider.Maximum - 1;
                    Send(RawPointerEventType.LeftButtonDown, 0, true, trackX: x, expectedHit: button);
                    Send(RawPointerEventType.LeftButtonUp, 0, false, trackX: x);
                    AssertSelected(direction < 0 ? slider.Minimum : slider.Maximum);
                    // The original theme must give visible feedback on both end regions.
                    slider.Value = Expected(0.5);
                    Send(RawPointerEventType.Move, 0.5, false);
                    PumpUi(TimeSpan.FromMilliseconds(30));
                    var normal = RenderColors(button);
                    Send(RawPointerEventType.Move, 0, false, trackX: x);
                    PumpUi(TimeSpan.FromMilliseconds(30));
                    Assert.True(button.IsPointerOver);
                    Assert.False(normal.SequenceEqual(RenderColors(button)), $"{kind} {button.Name} must show hover feedback.");
                    if (kind == AdjustmentKind.Diameter && Environment.GetEnvironmentVariable("GHOST_ADJUSTMENT_OUTPUT") is { Length: > 0 } output)
                        RenderLayout(flyout, output, $"{button.Name}-hover");
                }
                // The middle keeps click-to-position behavior instead of stepping.
                foreach (var fraction in new[] { 0.25, 0.75 })
                {
                    slider.Value = fraction < 0.5 ? slider.Maximum : slider.Minimum;
                    Send(RawPointerEventType.LeftButtonDown, fraction, true);
                    AssertSelected(Expected(fraction));
                    Send(RawPointerEventType.LeftButtonUp, fraction, false);
                    AssertSelected(Expected(fraction));
                }
                // The visible dot remains draggable even at the two limits, where it overlaps a step end.
                foreach (var fraction in new[] { 0d, 1d, 0.5 })
                {
                    slider.Value = Expected(fraction);
                    var thumbFraction = (slider.Value - slider.Minimum) / (slider.Maximum - slider.Minimum);
                    Send(RawPointerEventType.LeftButtonDown, thumbFraction, true, y: 2, expectedHit: track.Thumb);
                    foreach (var next in new[] { 0.75, 0.25, -0.2, 1.2, 0.5 })
                    {
                        Send(RawPointerEventType.Move, next, true, y: -12);
                        AssertSelected(Expected(next));
                    }
                    Send(RawPointerEventType.LeftButtonUp, 0.5, false, y: -12);
                    var released = slider.Value;
                    Send(RawPointerEventType.Move, 0.1, false);
                    AssertSelected(released);
                }
                // A middle-track press also supports native click-and-drag.
                Send(RawPointerEventType.LeftButtonDown, 0.25, true);
                Send(RawPointerEventType.Move, 0.75, true);
                AssertSelected(Expected(0.75));
                Send(RawPointerEventType.LeftButtonUp, 0.75, false);
                slider.Focus();
                foreach (var key in new[] { Key.Home, Key.End })
                {
                    slider.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
                    AssertSelected(Expected(key == Key.Home ? 0 : 1));
                }
                flyout.Hide();
            }
        }
        finally { owner.Close(); }
    }

    private static SkiaSharp.SKColor[] RenderColors(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!;
        using var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(root.Bounds.Width), (int)Math.Ceiling(root.Bounds.Height)), new Vector(96, 96));
        image.Render(root);
        using var stream = new MemoryStream();
        image.Save(stream);
        stream.Position = 0;
        using var pixels = SkiaSharp.SKBitmap.Decode(stream);
        var origin = control.TranslatePoint(new Point(), root)!.Value;
        return Enumerable.Range(0, (int)control.Bounds.Height)
            .SelectMany(y => Enumerable.Range(0, (int)control.Bounds.Width).Select(x => pixels.GetPixel((int)origin.X + x, (int)origin.Y + y))).ToArray();
    }

    private static void VerifyFrameRateDescriptions(SettingsWindow owner)
    {
        foreach (var language in new[] { UiLanguage.Chinese, UiLanguage.English })
        foreach (int? hertz in new int?[] { 60, 75, 240, null })
        {
            using var flyout = new SettingsAdjustmentFlyout(new AppSettings { Language = language }, AdjustmentKind.FrameRate, _ => { }, new FixedDisplay(hertz));
            flyout.OpenAt(owner);
            for (var index = 0; index < 4; index++)
            {
                flyout.FrameRateSlider!.Value = index;
                var expected = Math.Min(hertz ?? 60, index == 0 ? 120 : (index + 1) * 30);
                Assert.Equal(expected, flyout.Decision.TargetFramesPerSecond);
                Assert.Equal(TimeSpan.FromSeconds(1d / expected), GetTimer(flyout).Interval);
                var texts = flyout.View.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                Assert.Contains(string.Format(UiText.Text(language, "adjustFpsSelection"), SettingAdjustment.Format(flyout.Draft, AdjustmentKind.FrameRate, language)), texts);
                var status = string.Format(UiText.Text(language, "adjustFpsTarget"), expected) + " · " + (hertz is null
                    ? UiText.Text(language, "adjustFpsFallback") : string.Format(UiText.Text(language, "adjustFpsDisplay"), hertz));
                Assert.Contains(status, texts);
                Assert.Contains(UiText.Text(language, "adjustFpsHint"), texts);
                var labels = flyout.View.GetLogicalDescendants().OfType<Canvas>().Single().Children.OfType<TextBlock>().ToArray();
                for (var tick = 0; tick < labels.Length; tick++)
                    Assert.Equal(tick == index ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal, labels[tick].FontWeight);
                Assert.DoesNotContain(flyout.View.GetLogicalDescendants().OfType<Button>(), button => button.Content is "播放动画" or "暂停动画" or "Play animation" or "Pause animation");
            }
            var directory = Environment.GetEnvironmentVariable("GHOST_ADJUSTMENT_OUTPUT");
            if (hertz is null && !string.IsNullOrEmpty(directory))
            {
                flyout.View.Width = 220;
                flyout.View.UpdateLayout();
                RenderLayout(flyout, directory, $"FrameRate-fallback-{language}");
            }
            flyout.View.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, UiText.Text(language, "resetSetting")))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(PeekFrameRateLimit.Auto, flyout.Draft.PeekFrameRateLimit);
            Assert.True(flyout.IsAnimationRunning);
            flyout.Hide();
        }
    }

    private static DispatcherTimer GetTimer(SettingsAdjustmentFlyout flyout) => (DispatcherTimer)typeof(SettingsAdjustmentFlyout)
        .GetField("_timer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(flyout)!;

    private static void PumpUi(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        DispatcherTimer.RunOnce(() => frame.Continue = false, duration);
        Dispatcher.UIThread.PushFrame(frame);
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
            if (kind == AdjustmentKind.FrameRate)
            {
                foreach (var width in new[] { 220d, 340 })
                {
                    flyout.View.Width = width;
                    flyout.View.UpdateLayout();
                    var ticks = flyout.View.GetLogicalDescendants().OfType<Canvas>().Single();
                    var labels = ticks.Children.OfType<TextBlock>().ToArray();
                    Assert.Equal(4, labels.Length);
                    for (var index = 0; index < labels.Length; index++)
                    {
                        Assert.InRange(labels[index].Bounds.X, 0, width - labels[index].Bounds.Width + 1);
                        if (index > 0) Assert.True(labels[index - 1].Bounds.Right < labels[index].Bounds.Left);
                    }
                    if (!string.IsNullOrEmpty(directory))
                        RenderLayout(flyout, directory, $"FrameRate-{language}-{theme}-{width}");
                }
            }
            if (!string.IsNullOrEmpty(directory) && kind is AdjustmentKind.DiameterStep or AdjustmentKind.SoftEdgeWidth or AdjustmentKind.NotificationWidth or AdjustmentKind.FrameRate or AdjustmentKind.BlurLevel)
                RenderLayout(flyout, directory, $"{kind}-{language}-{theme}");
            flyout.Hide();
        }
        Assert.Equal("GhostSlacking", UiText.Text(UiLanguage.Chinese, "title"));
        Assert.Equal("GhostSlacking", UiText.Text(UiLanguage.English, "title"));
    }

    private static void RenderLayout(SettingsAdjustmentFlyout flyout, string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var view = flyout.Presenter!;
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)(view.Bounds.Width * scale), (int)(view.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
            bitmap.Render(view);
            bitmap.Save(System.IO.Path.Combine(directory, $"{name}-{scale}.png"));
        }
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
    private sealed class FixedDisplay(int? hertz = 75) : IDisplayRefreshRateProvider
    {
        public DisplayRefreshRateInfo? GetForPoint(System.Drawing.Point point) => hertz is { } rate ? new("test", rate) : null;
        public void Invalidate() { }
    }
}
