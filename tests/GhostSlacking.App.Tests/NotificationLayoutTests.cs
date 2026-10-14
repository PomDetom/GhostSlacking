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
}
