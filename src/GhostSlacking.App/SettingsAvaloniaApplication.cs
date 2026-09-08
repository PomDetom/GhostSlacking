using Avalonia;
using Avalonia.Styling;
using FluentAvalonia.Styling;

namespace GhostSlacking.App;

internal sealed class SettingsAvaloniaApplication : Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentAvaloniaTheme());
        RequestedThemeVariant = ThemeVariant.Default;
    }
}
