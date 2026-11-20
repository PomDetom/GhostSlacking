using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;
using GhostSlacking.App;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

internal static class SettingsMotionTests
{
    internal static void VerifyUi(UiMotionSettings preference, Action<bool> setAnimationsEnabled)
    {
        var window = new SettingsWindow(new AppSettings(), _ => true) { ShowActivated = false };
        var openingNavigation = Assert.IsType<NavigationView>(window.Content);
        var preparedBeforeShow = false;
        window.AddHandler(Window.WindowOpenedEvent, (_, _) =>
        {
            preparedBeforeShow = openingNavigation.Opacity < 1 && openingNavigation.RenderTransform?.Value.M32 > 0;
        });
        window.ShowWithEntrance();
        try
        {
            var navigation = Assert.IsType<NavigationView>(window.Content);
            var titleBar = Assert.Single(window.GetVisualDescendants().OfType<Panel>(), panel => panel.Name == "DefaultTitleBar");
            Assert.True(preparedBeforeShow);
            var openingSize = window.ClientSize;
            Assert.InRange(openingSize.Width, 839, 841);
            Assert.InRange(openingSize.Height, window.MinHeight - 1, window.MinHeight + window.TitleBar.Height + 1);
            Assert.True(double.IsPositiveInfinity(window.MaxWidth));
            Assert.True(double.IsPositiveInfinity(window.MaxHeight));
            Assert.Equal(1, titleBar.Opacity);
            var titleBarBounds = titleBar.Bounds;
            WaitFor(window, () => navigation.Opacity is > 0.52 and < 1 && navigation.RenderTransform?.Value.M32 > 0,
                "opening settings client animation");
            Assert.Equal(titleBarBounds, titleBar.Bounds);
            Assert.Equal(1, titleBar.Opacity);
            Capture(window, "settings-opening");
            WaitFor(window, () => navigation.Opacity == 1 && navigation.RenderTransform?.Value.M32 == 0, "settled settings entrance");
            Assert.Equal(openingSize, window.ClientSize);
            Assert.Equal(titleBarBounds, titleBar.Bounds);
            var heading = Field<StackPanel>(window, "_pageHeader");
            var pageHost = Field<ContentControl>(window, "_pageHost");
            var pages = Field<Dictionary<string, Control>>(window, "_pages");
            var pane = Assert.Single(navigation.GetVisualDescendants().OfType<Panel>(), panel => panel.Name == "PART_PaneRoot");
            var selection = Field<Border>(window, "_selectionBackground");
            var offset = Assert.IsType<TranslateTransform>(selection.RenderTransform);
            var paneHeader = Assert.Single(navigation.GetVisualDescendants().OfType<Grid>(), grid => grid.Name == "PaneToggleButtonGrid");
            var paneTitle = Assert.Single(paneHeader.GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "PaneTitleTextBlock");
            var paneToggle = Assert.Single(paneHeader.GetVisualDescendants().OfType<Button>(), button => button.Name == "TogglePaneButton");
            var iconHost = Assert.Single(paneToggle.GetVisualDescendants().OfType<Viewbox>(), viewbox => viewbox.Name == "IconHost");
            var paneIcon = Assert.IsType<Avalonia.Controls.Shapes.Path>(iconHost.Child);
            var paneShadow = Assert.Single(navigation.GetVisualDescendants().OfType<Border>(), border => border.Name == "ShadowCaster");
            Assert.Equal(1, pageHost.Opacity);
            Assert.False(heading.IsAnimating(Visual.OpacityProperty));
            Assert.InRange(pane.Bounds.Width, 55, 57);
            AssertHeaderClipped(paneHeader, pane);
            Assert.Equal(0, paneTitle.Opacity);
            Assert.True(paneIcon.IsEffectivelyVisible);
            Assert.True(paneIcon.TranslatePoint(new Point(8, 8), pane) is { X: > 0 and < 56 });
            Assert.True(selection.IsVisible);
            Assert.False(selection.IsHitTestVisible);
            Assert.Same(selection, pane.Children[0]);
            Assert.True(pane.ClipToBounds);
            Assert.False(paneShadow.IsVisible);
            Assert.Equal(new Thickness(1), selection.BorderThickness);
            Assert.True(selection.BoxShadow.Count > 0);
            var originalSelectionY = offset.Y;

            Select(navigation, "general");
            Assert.Same(pages["general"], pageHost.Content);
            WaitFor(window, () => pageHost.Opacity < 1 && heading.Opacity < 1 && pageHost.RenderTransform?.Value.M32 > 0,
                "page and heading fade with slide");
            var selected = Assert.IsType<NavigationViewItem>(navigation.SelectedItem);
            var rowBackground = SelectionRow(selected);
            var rowLabel = Assert.Single(rowBackground.GetVisualDescendants().OfType<ContentPresenter>(),
                presenter => presenter.Name == "ContentPresenter" && presenter.TemplatedParent is NavigationViewItemPresenter);
            Assert.Equal(0, rowLabel.Opacity);
            var selectedY = rowBackground.TranslatePoint(default, pane)!.Value.Y;
            WaitFor(window, () => offset.Y > originalSelectionY && offset.Y < selectedY, "whole background moving between navigation items");
            Capture(window, "selection-slide");
            Assert.InRange(selection.Width, pane.Bounds.Width / 2, rowBackground.Bounds.Width);
            Assert.Equal(rowBackground.Bounds.Height, selection.Height);
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(rowBackground.Background).Color.A);

            var combo = Field<ComboBox>(window, "_language");
            window.UpdateLayout();
            var arrow = Assert.Single(combo.GetVisualDescendants().OfType<Border>(), border => border.Name == "SettingsDropDownArrow");
            var rotation = Assert.IsType<RotateTransform>(arrow.Child!.RenderTransform);
            var popup = Assert.Single(combo.GetVisualDescendants().OfType<Popup>(), popup => popup.Name == "PART_Popup");
            var list = Assert.IsAssignableFrom<Control>(popup.Child);
            var originalChoice = combo.SelectedItem;
            combo.IsDropDownOpen = true;
            combo.IsDropDownOpen = false;
            Assert.False(popup.IsOpen);
            Assert.False(list.IsAnimating(Visual.OpacityProperty));
            combo.IsDropDownOpen = true;
            WaitFor(window, () => list.Opacity < 1 && list.RenderTransform?.Value.M11 < 1, "reopened combo fade with scale");
            combo.IsDropDownOpen = false;
            Assert.Same(originalChoice, combo.SelectedItem);

            paneToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(navigation.IsPaneOpen);
            window.UpdateLayout();
            Assert.Equal(0, paneTitle.Opacity);
            WaitFor(window, () => pane.Bounds.Width is > 75 and < 155, "sidebar opening width");
            AssertHeaderClipped(paneHeader, pane);
            var openingClip = Assert.IsType<RectangleGeometry>(paneHeader.Clip);
            Assert.Equal(0, rowLabel.Opacity);
            AssertTitleFullyVisibleOrHidden(paneHeader, paneTitle);
            Capture(window, "sidebar-opening");
            navigation.IsPaneOpen = false;
            WaitFor(window, () => pane.Bounds.Width is > 56 and < 155 && paneTitle.Opacity == 0,
                "sidebar closing width and hidden title");
            AssertHeaderClipped(paneHeader, pane);
            navigation.IsPaneOpen = true;
            WaitFor(window, () => Math.Abs(pane.Bounds.Width - 184) < 1 && !pane.IsAnimating(Layoutable.WidthProperty), "final sidebar width");
            AssertHeaderClipped(paneHeader, pane);
            Assert.NotSame(openingClip, paneHeader.Clip);
            Assert.Equal(1, paneTitle.Opacity);
            Assert.Equal(1, rowLabel.Opacity);
            Assert.Equal("设置", paneTitle.Text);
            AssertTitleFullyVisibleOrHidden(paneHeader, paneTitle);
            WaitFor(window, () => TitlePointHitsToggle(window, paneTitle, paneToggle), "expanded title inside the compositor's hit-test area");
            Capture(window, "sidebar-open");

            combo.SelectedIndex = 1;
            window.UpdateLayout();
            Assert.Equal("Settings", paneTitle.Text);
            Assert.Equal(1, paneTitle.Opacity);
            AssertTitleFullyVisibleOrHidden(paneHeader, paneTitle);
            combo.SelectedIndex = 0;

            Select(navigation, "reveal");
            window.UpdateLayout();
            var rows = Field<Dictionary<AdjustmentKind, SettingsAdjustmentRow>>(window, "_adjustmentValues");
            var row = rows[AdjustmentKind.Diameter];
            window.OpenAdjustment(AdjustmentKind.Diameter, row.Row);
            var adjustment = Assert.IsType<SettingsAdjustmentFlyout>(window.ActiveAdjustment);
            WaitFor(window, () => adjustment.Presenter!.Opacity < 1, "adjustment fade");
            window.NavigateToAbout();
            Assert.False(adjustment.IsOpen);
            Assert.Null(window.ActiveAdjustment);
            Assert.Same(pages["about"], pageHost.Content);
            WaitFor(window, () => pageHost.Opacity < 1, "About page fade");

            setAnimationsEnabled(false);
            Assert.False(preference.Refresh());
            Assert.False(pageHost.IsAnimating(Visual.OpacityProperty));
            Assert.Equal(1, heading.Opacity);
            Assert.Equal(0, pageHost.RenderTransform!.Value.M32);
            navigation.IsPaneOpen = false;
            Assert.False(pane.IsAnimating(Layoutable.WidthProperty));
            Select(navigation, "general");
            Assert.Same(pages["general"], pageHost.Content);
            Assert.Equal(1, pageHost.Opacity);
            window.UpdateLayout();
            Assert.InRange(pane.Bounds.Width, 55, 57);
            AssertHeaderClipped(paneHeader, pane);
            Assert.Equal(0, paneTitle.Opacity);
            Assert.True(paneIcon.IsEffectivelyVisible);
            Assert.Equal(0, rowLabel.Opacity);
            navigation.IsPaneOpen = true;
            window.UpdateLayout();
            Assert.Equal(1, paneTitle.Opacity);
            Assert.False(pane.IsAnimating(Layoutable.WidthProperty));
            navigation.IsPaneOpen = false;
            window.UpdateLayout();
            Assert.Equal(rowBackground.TranslatePoint(default, pane)!.Value.Y, offset.Y);
            Assert.False(offset.IsAnimating(TranslateTransform.YProperty));
            combo.IsDropDownOpen = true;
            Assert.True(popup.IsOpen);
            Assert.Equal(1, list.Opacity);
            Assert.Equal(1, list.RenderTransform!.Value.M11);
            Assert.False(list.IsAnimating(Visual.OpacityProperty));
            Assert.Equal(180, rotation.Angle);
            combo.IsDropDownOpen = false;
            Assert.Equal(0, rotation.Angle);
        }
        finally
        {
            window.Close();
            setAnimationsEnabled(true);
            preference.Refresh();
        }

        setAnimationsEnabled(false);
        try
        {
            var noMotionWindow = new SettingsWindow(new AppSettings(), _ => true) { ShowActivated = false };
            try
            {
                noMotionWindow.ShowWithEntrance();
                UiSceneWait.Commit(noMotionWindow, TimeSpan.FromMilliseconds(30));
                var navigation = Assert.IsType<NavigationView>(noMotionWindow.Content);
                Assert.Equal(1, navigation.Opacity);
                Assert.Null(navigation.RenderTransform);
            }
            finally { noMotionWindow.Close(); }
        }
        finally
        {
            setAnimationsEnabled(true);
            preference.Refresh();
        }

        VerifyMenuStyleScope();
    }

    private static void VerifyMenuStyleScope()
    {
        var menu = new NativeMenu { Items = { new NativeMenuItem("Settings") } };
        var otherMenu = new NativeMenu { Items = { new NativeMenuItem("Other") } };
        var matching = new MenuFlyoutPresenter { ItemsSource = menu.Items };
        var unrelated = new MenuFlyoutPresenter { ItemsSource = otherMenu.Items };
        var window = new Window { Content = new StackPanel { Children = { matching, unrelated } }, ShowActivated = false };
        var styles = Application.Current!.Styles;
        var style = GhostApplicationController.CreateTrayMenuMotion(menu);
        styles.Add(style);
        try
        {
            window.Show();
            Assert.Equal(145, matching.GetValue(UiEntrance.DurationProperty));
            Assert.Equal(0, unrelated.GetValue(UiEntrance.DurationProperty));
            Assert.False(unrelated.IsAnimating(Visual.OpacityProperty));
            WaitFor(window, () => matching.Opacity < 1 && matching.RenderTransform?.Value.M11 < 1, "tray menu fade with scale");
            WaitFor(window, () => !matching.IsAnimating(Visual.OpacityProperty), "completed tray menu fade");
            Assert.Equal(1, matching.Opacity);
            Assert.Equal(1, matching.RenderTransform!.Value.M11);
        }
        finally
        {
            window.Close();
            styles.Remove(style);
        }
    }

    private static void WaitFor(Window window, Func<bool> ready, string expectation)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!ready() && stopwatch.Elapsed < TimeSpan.FromSeconds(2))
        {
            UiSceneWait.Commit(window, TimeSpan.FromMilliseconds(30));
        }

        Assert.True(ready(), $"Did not observe {expectation}.");
    }

    private static void Select(NavigationView navigation, string page) => navigation.SelectedItem =
        navigation.MenuItems.Cast<NavigationViewItem>().Single(item => Equals(item.Tag, page));

    private static Border SelectionRow(NavigationViewItem item) => Assert.Single(item.GetVisualDescendants().OfType<Border>(),
        border => border.Name == "LayoutRoot" && border.TemplatedParent is NavigationViewItemPresenter);

    private static void AssertHeaderClipped(Grid header, Panel pane)
    {
        var edge = pane.TranslatePoint(new Point(pane.Bounds.Width, 0), header)!.Value.X;
        var expectedWidth = Math.Clamp(edge, 0, header.Bounds.Width);
        if (expectedWidth == header.Bounds.Width)
        {
            Assert.Null(header.Clip);
            return;
        }
        Assert.Equal(expectedWidth, Assert.IsType<RectangleGeometry>(header.Clip).Rect.Width, precision: 3);
    }

    private static void AssertTitleFullyVisibleOrHidden(Grid header, TextBlock title)
    {
        if (title.Opacity == 0) return;
        Assert.Equal(1, title.Opacity);
        var visibleWidth = (header.Clip as RectangleGeometry)?.Rect.Right ?? header.Bounds.Width;
        var right = title.TranslatePoint(new Point(title.Bounds.Width, 0), header)!.Value.X;
        Assert.True(right <= visibleWidth, $"The title extends to {right}, outside the visible width of {visibleWidth}.");
    }

    private static bool TitlePointHitsToggle(Window window, TextBlock title, Button toggle) =>
        title.TranslatePoint(new Point(title.Bounds.Width / 2, title.Bounds.Height / 2), window) is { } point &&
        window.InputHitTest(point) is Visual hit && hit.GetVisualAncestors().Prepend(hit).Contains(toggle);

    private static void Capture(Window window, string name)
    {
        var output = Environment.GetEnvironmentVariable("GHOST_SETTINGS_MOTION_OUTPUT");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.Bounds.Size, 1));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(output, name + ".png"));
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
}
