using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using FluentAvalonia.Styling;
using GhostSlacking.App;

namespace LiquidGlassSample;

internal static class Program
{
    internal static bool VerificationFailed { get; set; }
    [STAThread]
    public static int Main(string[] args)
    {
        var code = AppBuilder.Configure<SampleApplication>().UseWin32().UseSkia()
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
        return VerificationFailed ? 1 : code;
    }
}

internal sealed class SampleApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentAvaloniaTheme
        {
            PreferSystemTheme = false,
            PreferUserAccentColor = false,
            CustomAccentColor = AppTheme.AccentColor
        });
        RequestedThemeVariant = ThemeVariant.Light;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new SampleWindow(desktop.Args ?? []);
        base.OnFrameworkInitializationCompleted();
    }
}
