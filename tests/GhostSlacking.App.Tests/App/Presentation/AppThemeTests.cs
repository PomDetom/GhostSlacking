using Avalonia.Media;
using Avalonia.Styling;
using GhostSlacking.App;
using GhostSlacking.Core;

namespace GhostSlacking.App.Tests;

public sealed class AppThemeTests
{
    [Fact]
    public void Accent_color_matches_the_application_icon() => Assert.Equal(Color.Parse("#1CB2A5"), AppTheme.AccentColor);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hud_uses_the_settings_surface_border_and_text_for_every_severity(bool dark)
    {
        var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var settings = AppTheme.Palette(theme);
        foreach (var severity in Enum.GetValues<UserNotificationSeverity>())
        {
            var notification = AvaloniaNotificationService.ResolvePalette(theme, severity);
            Assert.Equal(settings.Surface, notification.Surface);
            Assert.Equal(settings.Border, notification.Border);
            Assert.Equal(settings.Primary, notification.Message);
        }
        Assert.Equal(settings.Success, AvaloniaNotificationService.ResolvePalette(theme, UserNotificationSeverity.Success).Icon);
        Assert.Equal(settings.Error, AvaloniaNotificationService.ResolvePalette(theme, UserNotificationSeverity.Error).Icon);
    }

    [Theory]
    [InlineData(UiThemeMode.System)]
    [InlineData(UiThemeMode.Light)]
    [InlineData(UiThemeMode.Dark)]
    public void Theme_modes_map_to_the_expected_requested_variant(UiThemeMode mode)
    {
        Assert.Equal(mode switch
        {
            UiThemeMode.Light => ThemeVariant.Light,
            UiThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        }, AppTheme.RequestedVariant(mode));
    }
}
