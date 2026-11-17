using Avalonia;
using Avalonia.Controls;
using SkiaSharp;

namespace LiquidGlassSample;

// The fixture owns the same immutable pixels its HWND displays. Sampling this surface
// never reads the desktop, another process, or a previous composited overlay.
internal sealed class Fixture : IDisposable
{
    private readonly SKImage _image = DrawMap();
    public required ShaderView Stage { get; init; }
    public double Phase { get; set; }
    public bool Refract { get; set; } = true;
    public bool Dark { get; set; }

    public SKShader Background(Control local)
    {
        var bounds = RelativeRect(Stage, local);
        if (bounds.Width <= 0 || bounds.Height <= 0) return SKShader.CreateColor(SKColors.SlateGray);
        var scale = (float)Math.Max(bounds.Width / (_image.Width - 96), bounds.Height / _image.Height);
        var left = bounds.X + (bounds.Width - _image.Width * scale) / 2 + 44 * scale * Math.Sin(Phase);
        var top = bounds.Y + (bounds.Height - _image.Height * scale) / 2;
        var matrix = SKMatrix.CreateScaleTranslation(scale, scale, (float)left, (float)top);
        var image = _image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, matrix);
        if (local == Stage || bounds.Contains(new Rect(local.Bounds.Size).Inflate(24))) return image;
        // Hard stops limit the source to the self-owned window even when the settings
        // window is dragged away. Beyond it we display a neutral, unsampled surface.
        using (image)
        using (var x = ClipAxis(new SKPoint((float)bounds.X, 0), new SKPoint((float)bounds.Right, 0)))
        using (var y = ClipAxis(new SKPoint(0, (float)bounds.Y), new SKPoint(0, (float)bounds.Bottom)))
        using (var mask = SKShader.CreateCompose(x, y, SKBlendMode.DstIn))
        using (var clipped = SKShader.CreateCompose(image, mask, SKBlendMode.DstIn))
        using (var empty = SKShader.CreateColor(new SKColor(69, 88, 98)))
            return SKShader.CreateCompose(empty, clipped, SKBlendMode.SrcOver);
    }

    private static SKShader ClipAxis(SKPoint start, SKPoint end) => SKShader.CreateLinearGradient(
        start, end, [SKColors.Transparent, SKColors.White, SKColors.White, SKColors.Transparent],
        [0f, 0.00001f, .99999f, 1f], SKShaderTileMode.Clamp);

    public static Rect RelativeRect(Control source, Control local)
    {
        var origin = local.PointToScreen(default);
        var start = source.PointToScreen(default);
        var end = source.PointToScreen(new Point(source.Bounds.Width, source.Bounds.Height));
        var scale = TopLevel.GetTopLevel(local)!.RenderScaling;
        return new Rect((start.X - origin.X) / scale, (start.Y - origin.Y) / scale,
            (end.X - start.X) / scale, (end.Y - start.Y) / scale);
    }

    private static SKImage DrawMap()
    {
        using var surface = SKSurface.Create(new SKImageInfo(1600, 1000));
        var canvas = surface.Canvas;
        using var gradient = SKShader.CreateLinearGradient(new SKPoint(120, 40), new SKPoint(1430, 860),
            new[] { SKColor.Parse("#193d51"), SKColor.Parse("#4597a1"), SKColor.Parse("#bce1cf"),
                SKColor.Parse("#ecba94"), SKColor.Parse("#a76570") }, null, SKShaderTileMode.Clamp);
        using var ink = new SKPaint { IsAntialias = true, Shader = gradient };
        canvas.DrawPaint(ink);
        ink.Shader = null;
        ink.Style = SKPaintStyle.Stroke;
        for (int i = -20; i < 62; i++)
        {
            using var path = new SKPath();
            for (int x = -8; x <= 1608; x += 8)
            {
                var y = (float)(i * 22 + 105 * Math.Sin(x / 292d + i * .052) + 38 * Math.Cos(x / 106d + i * .11));
                if (x == -8) path.MoveTo(x, y); else path.LineTo(x, y);
            }
            ink.Color = i % 5 == 0 ? new SKColor(255, 255, 249, 138) : new SKColor(17, 59, 74, 52);
            ink.StrokeWidth = i % 5 == 0 ? 2 : 0.9f;
            canvas.DrawPath(path, ink);
        }
        ink.Style = SKPaintStyle.Fill;
        ink.Color = new SKColor(255, 255, 255, 83);
        for (int x = 0; x < 1600; x += 64) canvas.DrawRect(x, 0, 1.2f, 1000, ink);
        for (int y = 0; y < 1000; y += 64) canvas.DrawRect(0, y, 1600, 1.2f, ink);
        using var face = SKTypeface.FromFamilyName("Consolas");
        ink.Typeface = face;
        ink.TextSize = 16;
        for (int y = 108; y < 1000; y += 192)
            for (int x = 34; x < 1600; x += 256)
            {
                ink.Color = new SKColor(18, 49, 57, 195);
                canvas.DrawText($"+  {x:0000} / {y:0000}", x, y, ink);
            }
        return surface.Snapshot();
    }

    public void Dispose() => _image.Dispose();
}
