using GhostSlacking.Core;

namespace GhostSlacking.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void Normalize_clamps_user_editable_values()
    {
        var settings = new AppSettings { RevealDiameterPx = 5000, PeekVirtualKey = 0 };

        var normalized = settings.Normalize();

        Assert.Equal(800, normalized.RevealDiameterPx);
        Assert.Equal(0x12, normalized.PeekVirtualKey);
    }

    [Fact]
    public void Normalize_falls_back_to_circle_for_unknown_shape()
    {
        var settings = new AppSettings { RevealShape = (RevealShape)999 };

        var normalized = settings.Normalize();

        Assert.Equal(RevealShape.Circle, normalized.RevealShape);
    }
}
