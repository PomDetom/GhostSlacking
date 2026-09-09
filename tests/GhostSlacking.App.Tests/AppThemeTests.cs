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

    [Fact]
    public void Dark_field_color_is_the_shared_neutral_notification_surface()
    {
        Assert.Equal(Color.Parse("#111111"), AppTheme.DarkFieldColor);
        Assert.Equal(
            AppTheme.DarkFieldColor,
            AvaloniaNotificationService.ResolvePalette(ThemeVariant.Dark, UserNotificationSeverity.Info).Surface);
    }

    [Theory]
    [InlineData(false, "#1CB2A5")]
    [InlineData(true, "#EF4444")]
    public void Dark_notification_palette_keeps_semantic_border_colors(
        bool error,
        string expectedBorder)
    {
        var severity = error ? UserNotificationSeverity.Error : UserNotificationSeverity.Info;
        var palette = AvaloniaNotificationService.ResolvePalette(ThemeVariant.Dark, severity);

        Assert.Equal(Color.Parse("#111111"), palette.Surface);
        Assert.Equal(Color.Parse(expectedBorder), palette.Border);
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
