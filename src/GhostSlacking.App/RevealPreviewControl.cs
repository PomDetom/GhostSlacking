using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal readonly record struct RevealPreviewComparison(int BeforeDiameter, int AfterDiameter, int FeatherWidth, bool IsStep, bool IsFeather)
{
    internal int ReferenceExtent => Math.Max(BeforeDiameter, AfterDiameter) + 2 * FeatherWidth + 48;
    internal static RevealPreviewComparison Create(AppSettings initial, AppSettings current, AdjustmentKind kind, int direction = 1)
    {
        var before = kind == AdjustmentKind.DiameterStep ? initial.RevealDiameterPx : current.RevealDiameterPx;
        var after = kind == AdjustmentKind.DiameterStep
            ? Math.Clamp(before + Math.Sign(direction) * current.RevealDiameterStepPx, 64, 800)
            : current.RevealDiameterPx;
        return new(before, after, current.RevealSoftEdgeWidthPx,
            kind == AdjustmentKind.DiameterStep, kind == AdjustmentKind.SoftEdgeWidth);
    }
}

// Sample pixels only: both comparison layers share one coordinate system and scale.
internal sealed class RevealPreviewControl : Control, IDisposable
{
    private WriteableBitmap? _mask;
    private string? _maskKey;
    private AppSettings _settings = new();
    private AdjustmentKind _kind;
    internal RevealPreviewComparison Comparison { get; private set; } = RevealPreviewComparison.Create(new(), new(), AdjustmentKind.Diameter);
    internal double AnimationSeconds { get; set; }
    internal double PreviewScale => Math.Min(Bounds.Width, Bounds.Height) / Comparison.ReferenceExtent;

    public RevealPreviewControl()
    {
        Height = 190;
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => { _maskKey = null; InvalidateVisual(); };
    }

    internal void Configure(AppSettings initial, AppSettings current, AdjustmentKind kind, int direction = 1)
    {
        _settings = current;
        _kind = kind;
        Comparison = RevealPreviewComparison.Create(initial, current, kind, direction);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Math.Max(1, (int)Math.Min(Bounds.Width, Bounds.Height));
        var key = $"{size}:{Comparison}:{_settings.RevealShape}:{_settings.RevealBlurLevel}:{ActualThemeVariant}";
        if (_maskKey != key)
        {
            _mask?.Dispose();
            _mask = CreateSample(size);
            _maskKey = key;
        }
        var origin = new Point((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        context.DrawImage(_mask!, new Rect(origin, new Size(size, size)));
        if (Comparison.IsStep || Comparison.IsFeather)
        {
            DrawOutline(context, origin, size, Comparison.BeforeDiameter,
                new Pen(new SolidColorBrush(AppTheme.Palette(ActualThemeVariant).Primary), 1.3));
            if (Comparison.IsStep)
                DrawOutline(context, origin, size, Comparison.AfterDiameter,
                    new Pen(new SolidColorBrush(AppTheme.AccentColor, 0.75), 1.5, new DashStyle([4d, 3d], 0)));
        }
        if (_kind == AdjustmentKind.FrameRate)
        {
            var x = origin.X + size * (0.5 + 0.38 * Math.Sin(AnimationSeconds * 1.5));
            context.DrawEllipse(new SolidColorBrush(AppTheme.AccentColor), null, new Point(x, origin.Y + size / 2d), 5, 5);
        }
    }

    private void DrawOutline(DrawingContext context, Point origin, int size, int diameter, Pen pen)
    {
        var scale = (double)size / Comparison.ReferenceExtent;
        var width = diameter * scale;
        var rect = new Rect(origin.X + (size - width) / 2, origin.Y + (size - width) / 2, width, width);
        if (_settings.RevealShape == RevealShape.Circle) context.DrawEllipse(null, pen, rect);
        else
        {
            var radius = _settings.RevealShape == RevealShape.RoundedRectangle ? Math.Clamp(diameter / 5, 12, 48) * scale : 0;
            context.DrawRectangle(null, pen, new RoundedRect(rect, radius));
        }
    }

    private WriteableBitmap CreateSample(int size)
    {
        var extent = Comparison.ReferenceExtent;
        var bounds = new System.Drawing.Rectangle(0, 0, extent, extent);
        var center = new System.Drawing.Point(extent / 2, extent / 2);
        var settings = new RevealSettings { DiameterPx = Comparison.AfterDiameter, Shape = _settings.RevealShape };
        var after = RevealGeometry.CreateReveal(bounds, center, settings);
        var before = RevealGeometry.CreateReveal(bounds, center, settings with { DiameterPx = Comparison.BeforeDiameter });
        var visual = RevealGeometry.CreateVisualState(0, bounds, after, Comparison.FeatherWidth, _settings.RevealBlurLevel);
        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        var dark = AppTheme.IsDark(ActualThemeVariant);
        using var frame = bitmap.Lock();
        var pixels = new byte[frame.RowBytes * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var wx = (x + 0.5f) * extent / size;
            var wy = (y + 0.5f) * extent / size;
            var distance = RevealGeometry.SignedDistanceFromBoundary(after, wx, wy);
            var opacity = distance <= 0 ? 1 : RevealGeometry.GetFeatherOpacity(distance, visual.FeatherWidthPx);
            var blur = distance <= 0 ? 0 : visual.BlurAmountPx;
            var background = dark ? 30 : 242;
            var baseColor = Color.FromRgb((byte)background, (byte)background, (byte)background);
            if (Comparison.IsStep && RevealGeometry.SignedDistanceFromBoundary(before, wx, wy) <= 0)
                baseColor = Sample(wx, wy, 0, dark);
            if (Comparison.IsStep) { opacity *= 0.42f; blur = Math.Max(3.2f, blur); }
            var color = Sample(wx, wy, blur, dark);
            var pixel = y * frame.RowBytes + x * 4;
            pixels[pixel] = (byte)(baseColor.B + (color.B - baseColor.B) * opacity);
            pixels[pixel + 1] = (byte)(baseColor.G + (color.G - baseColor.G) * opacity);
            pixels[pixel + 2] = (byte)(baseColor.R + (color.R - baseColor.R) * opacity);
            pixels[pixel + 3] = 255;
        }
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
        return bitmap;
    }

    private static Color Sample(float x, float y, float blur, bool dark)
    {
        static Color At(float x, float y, bool dark)
        {
            if ((int)(x / 35) % 2 == 0 && (int)(y / 35) % 2 == 0) return dark ? Color.FromRgb(18, 107, 102) : Color.FromRgb(107, 212, 200);
            if ((int)y % 40 is >= 12 and < 19) return dark ? Color.FromRgb(213, 229, 227) : Color.FromRgb(41, 79, 82);
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
