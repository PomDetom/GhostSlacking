using GhostSlacking.Core;

namespace GhostSlacking.Core.Tests;

public sealed class ReleaseVersionTests
{
    [Fact]
    public void Semantic_version_order_places_prereleases_before_stable()
    {
        Assert.True(ReleaseVersion.Parse("0.2.0-beta.2") > ReleaseVersion.Parse("0.2.0-beta.1"));
        Assert.True(ReleaseVersion.Parse("0.2.0-rc.1") > ReleaseVersion.Parse("0.2.0-beta.2"));
        Assert.True(ReleaseVersion.Parse("0.2.0") > ReleaseVersion.Parse("0.2.0-rc.1"));
    }

    [Fact]
    public void Release_version_preserves_prerelease_text_and_base_version()
    {
        var version = ReleaseVersion.Parse("1.4.3-beta.12");

        Assert.Equal("1.4.3-beta.12", version.Text);
        Assert.Equal("1.4.3", version.BaseVersionText);
        Assert.True(version.IsPreRelease);
    }

    [Theory]
    [InlineData("v1.2")]
    [InlineData("1.2.3-beta")]
    [InlineData("1.2.3-beta.01")]
    [InlineData("1.2.3+")]
    public void Invalid_release_versions_are_rejected(string text)
    {
        Assert.False(ReleaseVersion.TryParse(text, out _));
    }
}
