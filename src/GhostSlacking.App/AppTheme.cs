using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal static class AppTheme
{
    public static readonly Color AccentColor = Color.Parse("#1CB2A5");
    public static readonly Color DarkFieldColor = Color.Parse("#111111");

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
