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

// A stylized sample, using the same coordinates for the solid and ghosted layers.
// Skia is already supplied by Avalonia; no target-window pixels are read.
internal sealed class RevealPreviewControl : Control, IDisposable
{
    private WriteableBitmap? _image;
    private string? _imageKey;
    private AppSettings _settings = new();
    private AdjustmentKind _kind;
    internal RevealPreviewComparison Comparison { get; private set; } = RevealPreviewComparison.Create(new(), new(), AdjustmentKind.Diameter);
    internal double AnimationSeconds { get; set; }
    internal double PreviewScale => Math.Max(1, Math.Min(Bounds.Width, Bounds.Height) - 12) / Comparison.ReferenceExtent;
    internal bool IsBlurComparison => _kind == AdjustmentKind.BlurLevel;
    internal float BlurComparisonSigma => ComparisonSigma(_settings.RevealBlurLevel);

    public RevealPreviewControl()
    {
        Height = 200;
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => { _imageKey = null; InvalidateVisual(); };
    }

    internal static float ComparisonSigma(RevealBlurLevel level) => RevealGeometry.CreateVisualState(0,
        new System.Drawing.Rectangle(0, 0, 300, 300),
        RevealGeometry.CreateReveal(new System.Drawing.Rectangle(0, 0, 300, 300), new System.Drawing.Point(150, 150), new RevealSettings()),
        24, level).BlurAmountPx;

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
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(Bounds.Width * scaling)), Math.Max(1, (int)Math.Ceiling(Bounds.Height * scaling)));
        var key = $"{size}:{scaling}:{_kind}:{Comparison}:{_settings.RevealShape}:{_settings.RevealBlurLevel}:{ActualThemeVariant}";
        if (_imageKey != key)
        {
            _image?.Dispose();
            _image = CreateSample(size, scaling);
            _imageKey = key;
        }
        // Source rectangles are pixels; keep high-DPI backing pixels independent of layout DIPs.
        context.DrawImage(_image!, new Rect(_image!.PixelSize.ToSize(1)), new Rect(Bounds.Size));
        if (_kind == AdjustmentKind.FrameRate)
        {
            var x = Bounds.Width * (0.5 + 0.34 * Math.Sin(AnimationSeconds * 1.5));
            context.DrawEllipse(AdjustmentPalette.Brush(AdjustmentPalette.For(ActualThemeVariant).Accent), null,
                new Point(x, Bounds.Height / 2 + 8), 4, 4);
        }
    }

    private WriteableBitmap CreateSample(PixelSize size, double scaling)
    {
        var bitmap = new WriteableBitmap(size, new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        using var frame = bitmap.Lock();
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(size.Width, size.Height,
            SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul), frame.Address, frame.RowBytes);
        var canvas = surface.Canvas;
        canvas.Clear(SkiaSharp.SKColors.Transparent);
        canvas.Scale((float)scaling);
        if (IsBlurComparison)
        {
            var side = (float)Math.Min(112, Math.Min(Bounds.Width / 2 - 22, Bounds.Height - 72));
            var y = 58f;
            var left = SkiaSharp.SKRect.Create((float)Bounds.Width / 4 - side / 2, y, side, side);
            var right = SkiaSharp.SKRect.Create((float)Bounds.Width * 3 / 4 - side / 2, y, side, side);
            DrawSample(canvas, left, 14, 1, true);
            using var filter = SkiaSharp.SKImageFilter.CreateBlur(BlurComparisonSigma, BlurComparisonSigma);
            using var layer = new SkiaSharp.SKPaint { ImageFilter = filter };
            canvas.SaveLayer(layer);
            DrawSample(canvas, right, 14, 1, true);
            canvas.Restore();
        }
        else
        {
            var scale = (float)PreviewScale;
            var extent = Comparison.ReferenceExtent;
            canvas.Translate((float)Bounds.Width / 2, (float)Bounds.Height / 2 + 7);
            canvas.Scale(scale);
            canvas.Translate(-extent / 2f, -extent / 2f);
            var before = CenteredRect(Comparison.BeforeDiameter, extent);
            var after = CenteredRect(Comparison.AfterDiameter, extent);
            if (Comparison.IsStep)
            {
                DrawSample(canvas, before, Radius(Comparison.BeforeDiameter), scale);
                DrawGhost(canvas, after, Radius(Comparison.AfterDiameter), 1.5f / scale, 0.45f, scale);
                DrawGhostOutline(canvas, after, Radius(Comparison.AfterDiameter), scale);
            }
            else if (Comparison.IsFeather)
            {
                if (Comparison.FeatherWidth > 0)
                {
                    var outer = before;
                    outer.Inflate(Comparison.FeatherWidth * 0.55f, Comparison.FeatherWidth * 0.55f);
                    DrawGhost(canvas, outer, Radius(Comparison.BeforeDiameter) + Comparison.FeatherWidth * 0.55f,
                        Math.Max(0.5f, Comparison.FeatherWidth / 4f), 0.44f, scale);
                }
                DrawSample(canvas, before, Radius(Comparison.BeforeDiameter), scale);
            }
            else
            {
                DrawGhost(canvas, after, Radius(Comparison.AfterDiameter), 2 / scale, 0.18f, scale);
                DrawSample(canvas, after, Radius(Comparison.AfterDiameter), scale);
            }
        }
        surface.Flush();
        return bitmap;
    }

    private static SkiaSharp.SKRect CenteredRect(int diameter, int extent) =>
        SkiaSharp.SKRect.Create((extent - diameter) / 2f, (extent - diameter) / 2f, diameter, diameter);
    private float Radius(int diameter) => _settings.RevealShape == RevealShape.Circle ? diameter / 2f :
        _settings.RevealShape == RevealShape.Rectangle ? 0 : Math.Clamp(diameter / 5, 12, 48);
    private static SkiaSharp.SKColor ColorFor(Color color, float opacity = 1) => new(color.R, color.G, color.B, (byte)(255 * opacity));

    private void DrawSample(SkiaSharp.SKCanvas canvas, SkiaSharp.SKRect rect, float radius, float scale, bool detail = false)
    {
        var palette = AdjustmentPalette.For(ActualThemeVariant);
        using var fill = new SkiaSharp.SKPaint { IsAntialias = true, Color = ColorFor(palette.Soft) };
        using var path = new SkiaSharp.SKPath();
        path.AddRoundRect(rect, radius, radius);
        canvas.DrawPath(path, fill);
        canvas.Save();
        canvas.ClipPath(path, antialias: true);
        var lengths = new[] { 0.74f, 0.46f, 0.64f, 0.35f };
        for (var i = 0; i < lengths.Length; i++)
        {
            fill.Color = ColorFor(palette.Accent, i % 2 == 0 ? 0.52f : 0.28f);
            var bar = SkiaSharp.SKRect.Create(rect.Left + rect.Width * 0.13f,
                rect.Top + rect.Height * (0.26f + i * 0.16f), rect.Width * lengths[i], Math.Max(3, rect.Height * 0.045f));
            canvas.DrawRoundRect(bar, 2 / scale, 2 / scale, fill);
        }
        if (detail)
        {
            fill.Color = ColorFor(palette.Accent, 0.65f);
            fill.StrokeWidth = 0.9f;
            for (var i = 0; i < 5; i++)
                canvas.DrawLine(rect.Left + rect.Width * 0.13f, rect.Bottom - 14 + i * 2.2f,
                    rect.Right - rect.Width * 0.13f, rect.Bottom - 14 + i * 2.2f, fill);
        }
        canvas.Restore();
        fill.Style = SkiaSharp.SKPaintStyle.Stroke;
        fill.StrokeWidth = 0.65f / scale;
        fill.Color = ColorFor(palette.Accent, 0.45f);
        canvas.DrawPath(path, fill);
    }

    private void DrawGhost(SkiaSharp.SKCanvas canvas, SkiaSharp.SKRect rect, float radius, float sigma, float opacity, float scale)
    {
        using var filter = SkiaSharp.SKImageFilter.CreateBlur(sigma, sigma);
        using var layer = new SkiaSharp.SKPaint { ImageFilter = filter, Color = SkiaSharp.SKColors.White.WithAlpha((byte)(opacity * 255)) };
        canvas.SaveLayer(layer);
        DrawSample(canvas, rect, radius, scale);
        canvas.Restore();
    }

    private void DrawGhostOutline(SkiaSharp.SKCanvas canvas, SkiaSharp.SKRect rect, float radius, float scale)
    {
        using var filter = SkiaSharp.SKImageFilter.CreateBlur(0.65f / scale, 0.65f / scale);
        using var dash = SkiaSharp.SKPathEffect.CreateDash([2 / scale, 3 / scale], 0);
        using var pen = new SkiaSharp.SKPaint
        {
            IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 0.9f / scale,
            StrokeCap = SkiaSharp.SKStrokeCap.Round, Color = ColorFor(AdjustmentPalette.For(ActualThemeVariant).Accent, 0.5f),
            ImageFilter = filter, PathEffect = dash
        };
        canvas.DrawRoundRect(rect, radius, radius, pen);
    }

    public void Dispose() { _image?.Dispose(); _image = null; _imageKey = null; }
}
