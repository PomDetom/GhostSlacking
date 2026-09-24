using Avalonia;
using GhostSlacking.App;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

public sealed class NotificationLayoutTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void Relative_positions_keep_different_notification_sizes_inside_the_working_area(double scaling)
    {
        var area = new PixelRect(1920, -200, 1920, 1080);
        var shortPosition = NotificationLayout.Position(area, scaling, new Size(120, 40), new NotificationPlacement(1, 1));
        var longPosition = NotificationLayout.Position(area, scaling, new Size(360, 120), new NotificationPlacement(1, 1));

        Assert.Equal(area.Right - 16, shortPosition.X + (int)Math.Ceiling(120 * scaling));
        Assert.Equal(area.Bottom - 16, shortPosition.Y + (int)Math.Ceiling(40 * scaling));
        Assert.Equal(area.Right - 16, longPosition.X + (int)Math.Ceiling(360 * scaling));
        Assert.Equal(area.Bottom - 16, longPosition.Y + (int)Math.Ceiling(120 * scaling));
    }

    [Fact]
    public void Dragged_position_round_trips_as_relative_coordinates()
    {
        var area = new PixelRect(-1600, 0, 1600, 900);
        var size = new Size(230, 65);
        var original = new NotificationPlacement(0.3, 0.7);
        var point = NotificationLayout.Position(area, 1.25, size, original);
        var restored = NotificationLayout.FromPosition(area, 1.25, size, point);

        Assert.InRange(restored.X, 0.299, 0.301);
        Assert.InRange(restored.Y, 0.699, 0.701);
    }

    [Fact]
    public void Dragged_position_is_clamped_to_visible_bounds()
    {
        var placement = NotificationLayout.FromPosition(
            new PixelRect(0, 0, 1000, 800),
            1,
            new Size(200, 50),
            new PixelPoint(2000, -500));

        Assert.Equal(new NotificationPlacement(1, 0), placement);
    }

    [Fact]
    public void Chat_stack_keeps_the_newest_contiguous_rows_that_fit()
    {
        var visible = NotificationLayout.VisibleChatRows([100, 300, 100, 100, 100], 250, 4);

        Assert.Equal([3, 4], visible);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void Chat_stack_uses_pixel_heights_at_the_active_dpi(double scaling)
    {
        var heights = new[] { 70, 80, 90 }.Select(height => (int)Math.Ceiling(height * scaling)).ToArray();
        var availableHeight = (int)Math.Ceiling(180 * scaling);
        var gap = (int)Math.Ceiling(4 * scaling);

        Assert.Equal([1, 2], NotificationLayout.VisibleChatRows(heights, availableHeight, gap));
    }

    [Fact]
    public void Oversized_newest_chat_row_is_still_selected()
    {
        Assert.Equal([2], NotificationLayout.VisibleChatRows([20, 20, 500], 200, 4));
    }
}
