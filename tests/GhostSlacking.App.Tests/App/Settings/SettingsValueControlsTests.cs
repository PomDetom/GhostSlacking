using System.Reflection;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using GhostSlacking.App;
using GhostSlacking.Core;
using Path = Avalonia.Controls.Shapes.Path;

namespace GhostSlacking.App.Tests;

internal static class SettingsValueControlsTests
{
    // Runs in the solution's single STA fixture; never starts the tray controller
    // or writes the user's settings. Render outputs are opt-in build artifacts.
    internal static void VerifyUi()
    {
        var defaults = new AppSettings();
        var initial = defaults;
        foreach (var kind in Enum.GetValues<AdjustmentKind>())
        {
            var spec = SettingAdjustment.For(kind);
            var alternate = SettingAdjustment.Read(defaults, kind) == spec.Maximum ? spec.Minimum : spec.Maximum;
            initial = SettingAdjustment.Apply(initial, kind, alternate);
        }
        var failSave = true;
        var saveCalls = 0;
        AppSettings? saved = null;
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
            var rows = Field<Dictionary<AdjustmentKind, SettingsAdjustmentRow>>(window, "_adjustmentValues");
            var navigation = Field<NavigationView>(window, "_navigation");
            var saveBar = Field<SettingsSaveBar>(window, "_saveBar");
            Assert.Equal(11, rows.Count);
            foreach (var (kind, row) in rows)
            {
                Navigate(navigation, SettingAdjustment.IsNotification(kind) ? "notifications" : "reveal");
                var page = Assert.IsType<ScrollViewer>(Field<ContentControl>(window, "_pageHost").Content);
                Settle(window, 250);
                ScrollToRow(window, page, row);
                Assert.True(row.Reset.IsEnabled);
                Assert.Same(row.Row.Parent, row.Reset.Parent);
                Assert.DoesNotContain(row.Reset, row.Row.GetLogicalDescendants());
                Click(row.Row);
                var pending = window.ActiveAdjustment!;
                Assert.True(pending.IsOpen);
                Assert.Same(row.Row.Parent, pending.OverlayInputPassThroughElement);
                Settle(window);
                AssertHit(window, row.Reset, row.Reset);
                if (pending.NumberEditor is not null) pending.NumberEditor.Value = SettingAdjustment.For(kind).Minimum;
                Click(row.Reset);
                Assert.Null(window.ActiveAdjustment);
                Assert.False(pending.IsOpen);
                pending.Complete(); // A discarded local draft cannot overwrite the reset.
                var expected = SettingAdjustment.Apply(initial, kind, SettingAdjustment.Read(defaults, kind));
                Assert.Equal(expected.Normalize(), Current(window).Normalize());
                Assert.Equal(SettingAdjustment.Format(expected, kind, initial.Language), row.Value.Text);
                Assert.Equal(0, saveCalls);
                Click(row.Reset); // Reset remains usable when already at the default.
                Assert.Equal(expected.Normalize(), Current(window).Normalize());
                Click(Field<Button>(saveBar, "_cancel"));
                Assert.Equal(initial.Normalize(), Current(window).Normalize());
            }

            Navigate(navigation, "reveal");
            var diameter = rows[AdjustmentKind.Diameter];
            diameter.Row.Focus(NavigationMethod.Tab);
            diameter.Row.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.NotNull(window.ActiveAdjustment);
            Click(diameter.Reset);
            Assert.Null(window.ActiveAdjustment);
            Click(Field<Button>(saveBar, "_save"));
            Assert.Equal(SettingsStatus.Error, Field<SettingsEditState>(window, "_editState").Status);
            Assert.Equal(initial.Normalize(), Field<SettingsEditState>(window, "_editState").SavedSettings);
            Assert.Equal(defaults.RevealDiameterPx, Current(window).RevealDiameterPx);
            Assert.Null(saved);
            Click(Field<Button>(saveBar, "_cancel"));
            Assert.Equal(initial.Normalize(), Current(window).Normalize());
            failSave = false;
            Click(diameter.Reset);
            Click(Field<Button>(saveBar, "_save"));
            Assert.Equal(initial with { RevealDiameterPx = defaults.RevealDiameterPx }, saved);
            Assert.Equal(2, saveCalls);
            window.ApplyAdjustmentResult(initial, AdjustmentKind.Diameter);
            Click(Field<Button>(saveBar, "_cancel"));
            Assert.Equal(saved, Current(window).Normalize());

            window.ApplyAdjustmentResult(defaults, AdjustmentKind.Shape);
            window.ApplyAdjustmentResult(defaults, AdjustmentKind.FrameRate);
            VerifyLayouts(window, navigation, rows);
            VerifyComboKeyboard(window, navigation);
        }
        finally { window.Close(); }
    }

    private static void VerifyLayouts(SettingsWindow window, NavigationView navigation,
        Dictionary<AdjustmentKind, SettingsAdjustmentRow> rows)
    {
        var output = Environment.GetEnvironmentVariable("GHOST_SETTINGS_OUTPUT");
        foreach (var language in new[] { UiLanguage.Chinese, UiLanguage.English })
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        foreach (var expanded in new[] { false, true })
        foreach (var page in new[] { "reveal", "notifications", "general", "about" })
        {
            Field<ComboBox>(window, "_language").SelectedIndex = language == UiLanguage.Chinese ? 0 : 1;
            window.RequestedThemeVariant = theme;
            navigation.IsPaneOpen = expanded;
            Navigate(navigation, page);
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            Settle(window, 250);
            var content = Assert.IsType<ScrollViewer>(Field<ContentControl>(window, "_pageHost").Content);
            foreach (var (kind, row) in rows.Where(pair => page == (SettingAdjustment.IsNotification(pair.Key) ? "notifications" : "reveal")))
            {
                ScrollToRow(window, content, row);
                Assert.Equal(36, row.Field.Bounds.Height);
                Assert.Equal(36, row.Reset.Bounds.Height);
                var fieldRect = InWindow(row.Field, window);
                var resetRect = InWindow(row.Reset, window);
                Assert.InRange(resetRect.Left - fieldRect.Right, 7.9, 8.1);
                Assert.InRange(Math.Abs(resetRect.Center.Y - fieldRect.Center.Y), 0, 0.1);
                var groupWidth = language == UiLanguage.English ? 260 : 240;
                Assert.InRange(resetRect.Right - fieldRect.Left, groupWidth - 0.1, groupWidth + 0.1);
                Assert.All(row.Value.TextLayout.TextLines, line => Assert.False(line.HasCollapsed, row.Value.Text));
                Assert.Equal(SettingAdjustment.Format(Current(window), kind, language), row.Value.Text);
                Assert.Contains(UiText.Text(language, "resetSetting"), AutomationProperties.GetName(row.Reset));
                AssertHit(window, row.Row, row.Field);
                AssertHit(window, row.Reset, row.Reset);
                VerifyArrow(row.Field);
                Assert.NotNull(row.Field.Background);
                Assert.NotNull(row.Field.BorderBrush);
            }
            foreach (var combo in content!.GetLogicalDescendants().OfType<ComboBox>())
            {
                Assert.Equal(36, combo.Bounds.Height);
                VerifyArrow(combo);
            }
            if (string.IsNullOrEmpty(output)) continue;
            Directory.CreateDirectory(output);
            foreach (var scale in new[] { 1d, 1.5, 2 })
            {
                using var bitmap = new RenderTargetBitmap(
                    new PixelSize((int)(window.Bounds.Width * scale), (int)(window.Bounds.Height * scale)),
                    new Vector(96 * scale, 96 * scale));
                bitmap.Render(window);
                bitmap.Save(System.IO.Path.Combine(output, $"settings-{page}-{language}-{theme}-{(expanded ? "expanded" : "compact")}-{scale}.png"));
            }
        }
    }

    private static void VerifyArrow(Control control)
    {
        var arrow = Assert.Single(control.GetVisualDescendants().OfType<Border>(), child => child.Name == "SettingsDropDownArrow");
        var path = Assert.IsType<Path>(arrow.Child);
        Assert.Equal(new Size(12, 6), path.Bounds.Size);
        Assert.Equal(32, arrow.Bounds.Width);
        Assert.InRange(Math.Abs(path.Bounds.Center.X - arrow.Bounds.Width / 2), 0, 0.1);
        Assert.InRange(Math.Abs(path.Bounds.Center.Y - arrow.Bounds.Height / 2), 0, 0.1);
        Assert.NotNull(path.Stroke);
    }

    private static void VerifyComboKeyboard(SettingsWindow window, NavigationView navigation)
    {
        Navigate(navigation, "reveal");
        Settle(window);
        var combo = Field<ComboBox>(window, "_peekMode");
        combo.Focus(NavigationMethod.Tab);
        combo.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F4 });
        Assert.True(combo.IsDropDownOpen);
        combo.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.False(combo.IsDropDownOpen);
        var previous = combo.SelectedIndex;
        combo.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = previous == 0 ? Key.Down : Key.Up });
        Assert.NotEqual(previous, combo.SelectedIndex);
    }

    private static void AssertHit(Window window, Control expected, Control area)
    {
        // Layout and the renderer's hit-test scene are committed separately.
        // Wait for the scene instead of racing it after scrolling or navigation.
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(2))
        {
            if (window.InputHitTest(InWindow(area, window).Center) is Visual hit &&
                hit.GetVisualAncestors().Prepend(hit).Contains(expected)) return;
            Settle(window, 30);
        }
        Assert.Fail("Pointer did not reach " + AutomationProperties.GetName(expected));
    }

    private static Rect InWindow(Control control, Window window) => new(
        control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    private static void ScrollToRow(Window window, ScrollViewer page, SettingsAdjustmentRow row)
    {
        // Put the row above the floating save bar as well as inside the viewport.
        var top = row.Row.TranslatePoint(default, page)!.Value.Y + page.Offset.Y;
        page.Offset = new Vector(0, Math.Max(0, top - 40));
        Settle(window);
    }

    private static void Settle(Window window, int milliseconds = 60)
    {
        UiSceneWait.Commit(window, TimeSpan.FromMilliseconds(milliseconds));
        window.UpdateLayout();
    }

    private static void Navigate(NavigationView navigation, string page) => navigation.SelectedItem =
        navigation.MenuItems.Concat(navigation.FooterMenuItems).Cast<NavigationViewItem>().Single(item => Equals(item.Tag, page));
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static AppSettings Current(SettingsWindow window) => (AppSettings)typeof(SettingsWindow)
        .GetMethod("GetSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
}
