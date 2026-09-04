using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace GhostSlacking.App;

internal static class AppIcon
{
    public static Icon Instance { get; } = CreateIcon();

    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var background = new SolidBrush(Color.FromArgb(28, 178, 165));
        using var silhouette = new GraphicsPath();
        silhouette.AddArc(8, 6, 48, 48, 180, 180);
        silhouette.AddLine(56, 30, 56, 52);
        silhouette.AddBezier(56, 52, 52, 58, 47, 50, 42, 54);
        silhouette.AddBezier(42, 54, 37, 59, 32, 50, 27, 54);
        silhouette.AddBezier(27, 54, 22, 59, 17, 50, 8, 54);
        silhouette.CloseFigure();
        graphics.FillPath(background, silhouette);

        using var eye = new SolidBrush(Color.White);
        graphics.FillEllipse(eye, 22, 25, 7, 9);
        graphics.FillEllipse(eye, 38, 25, 7, 9);

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
