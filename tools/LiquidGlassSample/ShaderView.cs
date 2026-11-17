using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace LiquidGlassSample;

internal sealed class ShaderView(Func<ShaderView, MaterialFrame> create, Action<Exception>? failed = null,
    Action? drawn = null) : Control
{
    public override void Render(DrawingContext context)
    {
        if (Bounds.Width > 0 && Bounds.Height > 0 && TopLevel.GetTopLevel(this) != null)
        {
            try { context.Custom(new PaintShader(new Rect(Bounds.Size), create(this), failed, drawn)); }
            catch (Exception error) when (failed != null && error is InvalidOperationException or NotSupportedException)
            { failed(error); }
        }
    }

    private sealed class PaintShader(Rect bounds, MaterialFrame frame, Action<Exception>? failed,
        Action? drawn) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point point) => false;
        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
        public void Dispose() => frame.Dispose();
        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature == null)
            {
                failed?.Invoke(new NotSupportedException("Skia surface unavailable"));
                return;
            }
            using var lease = feature.Lease();
            try { frame.Draw(lease.SkCanvas, bounds); drawn?.Invoke(); }
            catch (Exception error) when (failed != null && error is InvalidOperationException or NotSupportedException)
            { failed(error); }

        }
    }
}
