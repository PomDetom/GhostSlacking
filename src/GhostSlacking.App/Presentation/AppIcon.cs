using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace GhostSlacking.App;

internal static class AppIcon
{
    private static readonly Lazy<WindowIcon> Icon = new(CreateIcon);
    private static readonly Lazy<Bitmap> TitleBarBitmap = new(CreateTitleBarBitmap);

    public static WindowIcon Instance => Icon.Value;
    public static Bitmap TitleBarImage => TitleBarBitmap.Value;

    private static WindowIcon CreateIcon()
    {
        using var stream = AssetLoader.Open(
            new Uri("avares://GhostSlacking.App/Assets/GhostSlacking.ico"));
        return new WindowIcon(stream);
    }

    private static Bitmap CreateTitleBarBitmap()
    {
        using var stream = AssetLoader.Open(
            new Uri("avares://GhostSlacking.App/Assets/GhostSlacking.ico"));
        return new Bitmap(stream);
    }
}
