using Avalonia;
using GhostSlacking.App;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

public sealed class NotificationLayoutTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void Hud_and_its_shadow_fit_all_position_extremes_at_the_active_dpi(double scaling)
    {
        var area = new PixelRect(-1600, -200, 1600, 900);
        foreach (var x in new[] { 0, 50, 100 })
            foreach (var y in new[] { 0, 50, 100 })
            {
                var geometry = NotificationLayout.Calculate(area, scaling, new NotificationHudSettings
                {
                    Width = 300,
                    Height = 96,
                    HorizontalPosition = x,
                    VerticalPosition = y
                });
                Assert.InRange(geometry.Position.X, area.X, area.Right - (int)Math.Ceiling(geometry.WindowSize.Width * scaling));
                Assert.InRange(geometry.Position.Y, area.Y, area.Bottom - (int)Math.Ceiling(geometry.WindowSize.Height * scaling));
            }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void Default_hud_is_bottom_centered_with_an_18_dip_gap(double scaling)
    {
        var area = new PixelRect(1920, -200, 1920, 1080);
        var geometry = NotificationLayout.Calculate(area, scaling, new NotificationHudSettings());
        var surfaceX = geometry.Position.X + (int)Math.Round(NotificationLayout.ShadowPadding * scaling);
        var surfaceY = geometry.Position.Y + (int)Math.Round(NotificationLayout.ShadowPadding * scaling);
        Assert.InRange(surfaceX + geometry.SurfaceSize.Width * scaling / 2, area.X + area.Width / 2.0 - 1, area.X + area.Width / 2.0 + 1);
        Assert.InRange(area.Bottom - surfaceY - geometry.SurfaceSize.Height * scaling, 18 * scaling - 1, 18 * scaling + 1);
    }

    [Fact]
    public void Narrow_working_area_reduces_rendered_size_without_changing_saved_preferences()
    {
        var settings = new NotificationHudSettings { Width = 300 };
        var geometry = NotificationLayout.Calculate(new PixelRect(0, 0, 320, 240), 2, settings);
        Assert.Equal(128, geometry.SurfaceSize.Width);
        Assert.True(geometry.WindowSize.Width * 2 <= 320);
        Assert.True(geometry.WindowSize.Height * 2 <= 240);
        Assert.Equal(300, settings.Width);
    }

    [Theory]
    [InlineData(20, 44, 64)]
    [InlineData(88, 44, 88)]
    [InlineData(20, 96, 96)]
    [InlineData(500, 44, 132)]
    public void Content_width_can_shrink_below_saved_minimum_but_is_bounded_by_height_and_cap(
        double desired, double height, double expected)
    {
        var settings = new NotificationHudSettings { Height = height };
        var geometry = NotificationLayout.Calculate(new PixelRect(0, 0, 1920, 1080), 1, settings, desired);
        Assert.Equal(expected, geometry.SurfaceSize.Width);
        Assert.Equal(height, geometry.SurfaceSize.Height);
        Assert.Equal(132, settings.Width);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void Short_and_long_messages_keep_the_selected_left_center_or_right_anchor_at_each_dpi(double scaling)
    {
        var area = new PixelRect(-1920, -200, 1920, 1080);
        foreach (var horizontal in new[] { 0, 50, 100 })
        foreach (var desired in new[] { 88, 500 })
        {
            var settings = new NotificationHudSettings { HorizontalPosition = horizontal };
            var geometry = NotificationLayout.Calculate(area, scaling, settings, desired);
            var left = geometry.Position.X + Math.Round(NotificationLayout.ShadowPadding * scaling);
            var width = Math.Ceiling(geometry.SurfaceSize.Width * scaling);
            var actual = horizontal switch { 0 => left, 50 => left + width / 2, _ => left + width };
            var expected = horizontal switch
            {
                0 => area.X + Math.Round(16 * scaling),
                50 => area.X + area.Width / 2.0,
                _ => area.Right - Math.Round(16 * scaling)
            };
            Assert.InRange(Math.Abs(actual - expected), 0, 1);
        }
    }

    [Fact]
    public void A_narrow_screen_takes_precedence_over_the_content_minimum_and_saved_width()
    {
        var settings = new NotificationHudSettings { Width = 100, Height = 96 };
        var geometry = NotificationLayout.Calculate(new PixelRect(0, 0, 160, 300), 2, settings, 500);
        Assert.Equal(48, geometry.SurfaceSize.Width);
        Assert.Equal(96, geometry.SurfaceSize.Height);
        Assert.Equal(100, settings.Width);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_invalid_content_measurement_uses_the_saved_cap(double desired)
    {
        var geometry = NotificationLayout.Calculate(new PixelRect(0, 0, 1920, 1080), 1, new(), desired);
        Assert.Equal(132, geometry.SurfaceSize.Width);
    }
}
