using Avalonia.Styling;
using Avalonia.Media;
using GhostSlacking.App;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

public sealed class AppThemeTests
{
    [Fact]
    public void Accent_color_matches_the_application_icon()
    {
        Assert.Equal(Color.Parse("#1CB2A5"), AppTheme.AccentColor);
    }

    [Theory]
    [InlineData(UiThemeMode.System)]
    [InlineData(UiThemeMode.Light)]
    [InlineData(UiThemeMode.Dark)]
    public void Theme_modes_map_to_the_expected_requested_variant(UiThemeMode mode)
    {
        var expected = mode switch
        {
            UiThemeMode.Light => ThemeVariant.Light,
            UiThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        Assert.Equal(expected, AppTheme.RequestedVariant(mode));
    }
}
