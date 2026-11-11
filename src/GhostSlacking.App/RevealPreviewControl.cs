using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GhostSlacking.Core;

namespace GhostSlacking.App;

// A fixed 1100px world keeps diameter and feather comparisons at the same scale.
// This simulates the appearance on sample content; it does not capture a window.
internal sealed class RevealPreviewControl : Control, IDisposable
{
    internal const int WorldSize = 1100;
    private WriteableBitmap? _mask;
    private string? _maskKey;
    private AppSettings _settings = new();
    private int _diameter = 144;
    internal double AnimationSeconds { get; set; }
    internal double PreviewScale => Math.Min(Bounds.Width, Bounds.Height) / WorldSize;

    public RevealPreviewControl()
    {
        Height = 280;
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => { _maskKey = null; InvalidateVisual(); };
    }

    internal void Update(AppSettings settings, int diameter)
    {
        _settings = settings;
        _diameter = diameter;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Math.Max(1, (int)Math.Min(Bounds.Width, Bounds.Height));
        var key = $"{size}:{_diameter}:{_settings.RevealSoftEdgeWidthPx}:{_settings.RevealShape}:{_settings.RevealBlurLevel}:{ActualThemeVariant}";
        if (_maskKey != key)
        {
            _mask?.Dispose();
            _mask = CreateSample(size);
            _maskKey = key;
        }
        var origin = new Point((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        context.DrawImage(_mask!, new Rect(origin, new Size(size, size)));
        // The marker travels at a fixed speed; only sampling cadence changes with FPS.
        var x = origin.X + size * (0.5 + 0.38 * Math.Sin(AnimationSeconds * 1.5));
        var y = origin.Y + size * 0.5;
        context.DrawEllipse(new SolidColorBrush(AppTheme.AccentColor), null, new Point(x, y), 5, 5);
    }

    private WriteableBitmap CreateSample(int size)
    {
        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        var region = RevealGeometry.CreateReveal(new System.Drawing.Rectangle(0, 0, WorldSize, WorldSize),
            new System.Drawing.Point(WorldSize / 2, WorldSize / 2),
            new RevealSettings { DiameterPx = _diameter, Shape = _settings.RevealShape, SoftEdgeWidthPx = _settings.RevealSoftEdgeWidthPx, BlurLevel = _settings.RevealBlurLevel });
        var visual = RevealGeometry.CreateVisualState(0, new System.Drawing.Rectangle(0, 0, WorldSize, WorldSize),
            region, _settings.RevealSoftEdgeWidthPx, _settings.RevealBlurLevel);
        var dark = AppTheme.IsDark(ActualThemeVariant);
        using var frame = bitmap.Lock();
        var pixels = new byte[frame.RowBytes * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var wx = (x + 0.5f) * WorldSize / size;
            var wy = (y + 0.5f) * WorldSize / size;
            var distance = RevealGeometry.SignedDistanceFromBoundary(region, wx, wy);
            var opacity = distance <= 0 ? 1 : RevealGeometry.GetFeatherOpacity(distance, visual.FeatherWidthPx);
            var blur = distance <= 0 ? 0 : visual.BlurAmountPx;
            var color = Sample(wx, wy, blur, dark);
            var background = dark ? 30 : 234;
            var pixel = y * frame.RowBytes + x * 4;
            pixels[pixel] = (byte)(background + (color.B - background) * opacity);
            pixels[pixel + 1] = (byte)(background + (color.G - background) * opacity);
            pixels[pixel + 2] = (byte)(background + (color.R - background) * opacity);
            pixels[pixel + 3] = 255;
        }
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
        return bitmap;
    }

    private static Color Sample(float x, float y, float blur, bool dark)
    {
        static Color At(float x, float y, bool dark)
        {
            if ((int)(x / 90) % 2 == 0 && (int)(y / 90) % 2 == 0) return dark ? Color.FromRgb(18, 107, 102) : Color.FromRgb(107, 212, 200);
            if ((int)y % 80 is >= 28 and < 42) return dark ? Color.FromRgb(213, 229, 227) : Color.FromRgb(41, 79, 82);
            return dark ? Color.FromRgb(60, 85, 89) : Color.FromRgb(229, 246, 243);
        }
        if (blur == 0) return At(x, y, dark);
        var r = 0; var g = 0; var b = 0;
        for (var iy = -2; iy <= 2; iy++)
        for (var ix = -2; ix <= 2; ix++)
        {
            var c = At(x + ix * blur, y + iy * blur, dark);
            r += c.R; g += c.G; b += c.B;
        }
        return Color.FromRgb((byte)(r / 25), (byte)(g / 25), (byte)(b / 25));
    }

    public void Dispose() { _mask?.Dispose(); _mask = null; _maskKey = null; }
}
