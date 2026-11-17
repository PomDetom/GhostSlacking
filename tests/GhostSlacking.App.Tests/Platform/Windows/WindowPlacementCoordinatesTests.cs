using System.Drawing;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App.Tests;

public sealed class WindowPlacementCoordinatesTests
{
    [Fact]
    public void Top_and_left_taskbar_offsets_round_trip_between_tool_and_normal_windows()
    {
        var display = new WindowDisplayContext("negative", new(-1920, -200, 1920, 1080), new(-1880, -160, 1880, 1040), 120, 125);
        var placement = new WindowPlacementSnapshot(2, 3, new(-1, -1), new(-1920, -200), new(-1800, -100, 800, 600), new(-1920, -200, 1920, 1080));
        var workspace = WindowPlacementCoordinates.Convert(placement, true, false, display);
        Assert.Equal(new Rectangle(-1840, -140, 800, 600), workspace.NormalPosition);
        Assert.Equal(new Point(-1, -1), workspace.MinPosition);
        Assert.Equal(new Point(-1960, -240), workspace.MaxPosition);
        Assert.Equal(placement, WindowPlacementCoordinates.Convert(workspace, false, true, display));
        Assert.Equal(placement, WindowPlacementCoordinates.Convert(placement, true, true, display));
    }
}
