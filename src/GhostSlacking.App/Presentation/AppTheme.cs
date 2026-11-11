using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal static class AppTheme
{
    public static readonly Color AccentColor = Color.Parse("#1CB2A5");
    public static readonly Color DarkFieldColor = Color.Parse("#111111");
    public static UiPalette Palette(ThemeVariant? variant)
    {
        var dark = IsDark(variant);
        return new UiPalette(
            Color.Parse(dark ? "#27292A" : "#FFFFFF"),
            Color.Parse(dark ? "#3A3E3F" : "#E0E6E7"),
            Color.Parse(dark ? "#F0F3F3" : "#202829"),
            Color.Parse(dark ? "#A4AFB1" : "#697577"),
            Color.Parse(dark ? "#55D5C7" : "#087F79"),
            Color.Parse(dark ? "#71DB9C" : "#198754"),
            Color.Parse(dark ? "#FF8A80" : "#C42B1C"));
    }

    public static ThemeVariant RequestedVariant(UiThemeMode mode) => mode switch
    {
        UiThemeMode.Light => ThemeVariant.Light,
        UiThemeMode.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default
    };

    public static void Apply(UiThemeMode mode)
    {
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = RequestedVariant(mode);
        }
    }

    public static bool IsDark(ThemeVariant? variant) => variant == ThemeVariant.Dark;
}

internal readonly record struct UiPalette(
    Color Surface, Color Border, Color Primary, Color Secondary, Color Accent, Color Success, Color Error);
