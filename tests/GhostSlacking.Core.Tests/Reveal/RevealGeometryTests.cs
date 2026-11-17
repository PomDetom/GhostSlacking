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
    [InlineData(-1)]
    [InlineData(129)]
    public void Rejects_invalid_soft_edge_width(int width)
    {
        var settings = new RevealSettings { SoftEdgeWidthPx = width };

        Assert.Throws<ArgumentException>(() =>
            RevealGeometry.CreateReveal(Rectangle.Empty, Point.Empty, settings));
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

    [Theory]
    [InlineData(RevealShape.Circle, 0, 0)]
    [InlineData(RevealShape.Rectangle, 0, 0)]
    [InlineData(RevealShape.RoundedRectangle, 48, 82)]
    public void Feather_visual_preserves_the_clear_core_and_expands_content_by_seventy_percent(
        RevealShape shape,
        int coreCornerRadius,
        int expectedContentCornerRadius)
    {
        var core = new CircleRegion(250, 200, 240, shape, coreCornerRadius);

        var visual = RevealGeometry.CreateVisualState(
            42,
            new Rectangle(100, 50, 800, 600),
            core,
            48,
            RevealBlurLevel.High);

        Assert.Equal(core, visual.CoreRegion);
        Assert.Equal(308, visual.ContentRegion.DiameterPx);
        Assert.Equal(expectedContentCornerRadius, visual.ContentRegion.CornerRadius);
        Assert.Equal(16F, visual.BlurAmountPx);
        Assert.Equal(new Rectangle(182, 82, 336, 336), RevealGeometry.GetFeatherBounds(visual));
    }

    [Fact]
    public void Feather_bounds_are_clipped_to_the_target_window_on_negative_coordinate_displays()
    {
        var visual = RevealGeometry.CreateVisualState(
            42,
            new Rectangle(-800, 100, 800, 600),
            new CircleRegion(10, 20, 240),
            48,
            RevealBlurLevel.Medium);

        Assert.Equal(new Rectangle(-800, 100, 178, 188), RevealGeometry.GetFeatherBounds(visual));
    }

    [Theory]
    [InlineData(0F, 0F)]
    [InlineData(3.6F, 0.5F)]
    [InlineData(7.2F, 1F)]
    [InlineData(33.6F, 1F)]
    [InlineData(40.8F, 0.5F)]
    [InlineData(48F, 0F)]
    public void Feather_opacity_blends_in_then_fades_out(float distance, float expectedOpacity)
    {
        Assert.Equal(expectedOpacity, RevealGeometry.GetFeatherOpacity(distance, 48), 3);
    }

    [Theory]
    [InlineData(RevealBlurLevel.Low, 3.2F)]
    [InlineData(RevealBlurLevel.Medium, 8.8F)]
    [InlineData(RevealBlurLevel.High, 16F)]
    public void Feather_uses_one_blur_amount_selected_by_the_user(
        RevealBlurLevel blurLevel,
        float expectedBlurAmount)
    {
        var visual = RevealGeometry.CreateVisualState(
            42,
            new Rectangle(0, 0, 800, 600),
            new CircleRegion(250, 200, 240),
            48,
            blurLevel);

        Assert.Equal(expectedBlurAmount, visual.BlurAmountPx, 3);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(48)]
    [InlineData(128)]
    public void Feather_opacity_is_continuous_at_transition_boundaries(int width)
    {
        const float epsilon = 0.001F;
        foreach (var boundary in new[] { 0.15F, 0.7F })
        {
            var before = RevealGeometry.GetFeatherOpacity((boundary - epsilon) * width, width);
            var after = RevealGeometry.GetFeatherOpacity((boundary + epsilon) * width, width);

            Assert.InRange(MathF.Abs(before - after), 0F, 0.001F);
        }
    }

    [Theory]
    [InlineData(RevealShape.Circle, 220F, 100F, 0F)]
    [InlineData(RevealShape.Rectangle, 220F, 100F, 0F)]
    [InlineData(RevealShape.RoundedRectangle, 220F, 100F, 0F)]
    [InlineData(RevealShape.Circle, 230F, 100F, 10F)]
    [InlineData(RevealShape.Rectangle, 230F, 100F, 10F)]
    public void Signed_distance_is_zero_on_the_core_edge_and_positive_outside(
        RevealShape shape,
        float x,
        float y,
        float expectedDistance)
    {
        var region = new CircleRegion(100, 100, 240, shape, 48);

        Assert.Equal(expectedDistance, RevealGeometry.SignedDistanceFromBoundary(region, x, y), 3);
    }
}
