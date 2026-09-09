using System.Drawing;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

public sealed class RevealOverlayLayoutTests
{
    [Fact]
    public void Cursor_movement_reuses_the_mask_and_only_changes_the_visual_offset_and_ring()
    {
        var first = RevealOverlayLayout.Create(CreateVisual(new Point(200, 180)));
        var second = RevealOverlayLayout.Create(CreateVisual(new Point(420, 310)));

        Assert.Equal(first.HostBounds, second.HostBounds);
        Assert.Equal(first.MaskKey, second.MaskKey);
        Assert.Equal(first.TemplateCoreRegion, second.TemplateCoreRegion);
        Assert.NotEqual(first.VisualOffset, second.VisualOffset);
        Assert.NotEqual(first.RingInnerRegion, second.RingInnerRegion);
    }

    [Fact]
    public void Window_edges_do_not_change_the_mask_template()
    {
        var center = RevealOverlayLayout.Create(CreateVisual(new Point(300, 250)));
        var topLeft = RevealOverlayLayout.Create(CreateVisual(new Point(100, 100)));
        var bottomRight = RevealOverlayLayout.Create(CreateVisual(new Point(599, 499)));

        Assert.Equal(center.MaskKey, topLeft.MaskKey);
        Assert.Equal(center.MaskKey, bottomRight.MaskKey);
        Assert.True(topLeft.VisualOffset.X < 0);
        Assert.True(topLeft.VisualOffset.Y < 0);
        Assert.True(bottomRight.VisualOffset.X + bottomRight.SurfaceSize.Width > bottomRight.HostBounds.Width);
        Assert.True(bottomRight.VisualOffset.Y + bottomRight.SurfaceSize.Height > bottomRight.HostBounds.Height);
    }

    [Fact]
    public void Settings_change_mask_key_while_window_resize_only_changes_host_bounds()
    {
        var original = RevealOverlayLayout.Create(CreateVisual(new Point(200, 180)));
        var resized = RevealOverlayLayout.Create(CreateVisual(
            new Point(200, 180),
            new Rectangle(100, 100, 900, 700)));
        var changed = RevealOverlayLayout.Create(CreateVisual(
            new Point(200, 180),
            diameter: 320,
            feather: 32,
            blur: 8F));

        Assert.Equal(original.MaskKey, resized.MaskKey);
        Assert.NotEqual(original.HostBounds, resized.HostBounds);
        Assert.NotEqual(original.MaskKey, changed.MaskKey);
    }

    private static RevealVisualState CreateVisual(
        Point cursor,
        Rectangle? windowBounds = null,
        int diameter = 144,
        int feather = 16,
        float blur = 4F)
    {
        var bounds = windowBounds ?? new Rectangle(100, 100, 500, 400);
        var core = new CircleRegion(
            cursor.X - bounds.Left,
            cursor.Y - bounds.Top,
            diameter,
            RevealShape.RoundedRectangle,
            28);
        return new RevealVisualState(42, bounds, core, core, feather, blur);
    }
}
