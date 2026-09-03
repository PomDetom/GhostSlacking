using GhostSlacking.Core;
using System.Drawing;

namespace GhostSlacking.Core.Tests;

public sealed class RevealGeometryTests
{
    [Fact]
    public void Converts_screen_cursor_to_window_local_coordinates()
    {
        var region = RevealGeometry.CreateCircle(new Rectangle(-100, 50, 800, 600), new Point(150, 250), 240);

        Assert.Equal(250, region.CenterX);
        Assert.Equal(200, region.CenterY);
        Assert.Equal(new Rectangle(130, 80, 240, 240), RevealGeometry.GetBounds(region));
    }

    [Fact]
    public void Intersects_reveal_region_with_window_bounds()
    {
        var region = new CircleRegion(10, 20, 100);

        Assert.Equal(new Rectangle(0, 0, 60, 70), RevealGeometry.IntersectWithWindow(region, new Rectangle(0, 0, 500, 500)));
    }

    [Fact]
    public void Rejects_invalid_diameter()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RevealGeometry.CreateCircle(Rectangle.Empty, Point.Empty, 0));
    }

    [Theory]
    [InlineData(RevealShape.Circle, 0)]
    [InlineData(RevealShape.Rectangle, 0)]
    [InlineData(RevealShape.RoundedRectangle, 48)]
    public void Creates_the_selected_reveal_shape(RevealShape shape, int expectedCornerRadius)
    {
        var settings = new RevealSettings { DiameterPx = 240, Shape = shape };

        var region = RevealGeometry.CreateReveal(new Rectangle(100, 50, 800, 600), new Point(350, 250), settings);

        Assert.Equal(shape, region.Shape);
        Assert.Equal(expectedCornerRadius, region.CornerRadius);
        Assert.Equal(new Rectangle(130, 80, 240, 240), RevealGeometry.GetBounds(region));
    }
}
