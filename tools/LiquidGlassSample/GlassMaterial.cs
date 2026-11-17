using Avalonia;
using SkiaSharp;

namespace LiquidGlassSample;

internal sealed class MaterialFrame : IDisposable
{
    private readonly SKPicture? _picture;
    private readonly SKShader? _image;
    internal MaterialFrame(SKPicture picture) => _picture = picture;
    private MaterialFrame(SKShader image) => _image = image;
    public static implicit operator MaterialFrame(SKShader image) => new(image);


    public void Draw(SKCanvas canvas, Rect bounds)
    {
        if (_picture != null) canvas.DrawPicture(_picture);
        else
        {
            using var paint = new SKPaint { Shader = _image, FilterQuality = SKFilterQuality.Low };
            canvas.DrawRect(0, 0, (float)bounds.Width, (float)bounds.Height, paint);
        }
    }

    public void Dispose() { _picture?.Dispose(); _image?.Dispose(); }
}

internal static class GlassMaterial
{
    private readonly record struct Profile(float Width, float Height, float Radius, float Band, bool Bend, float Blur, bool Feather);
    private sealed record Mesh(SKVertices Body, SKVertices Cool, SKVertices Warm, SKVertices Rim) : IDisposable
    {
        public void Dispose() { Body.Dispose(); Cool.Dispose(); Warm.Dispose(); Rim.Dispose(); }
    }
    // Record on the UI thread. Display lists retain native references across cache eviction.
    private static readonly Dictionary<Profile, Mesh> Cache = [];

    internal static MaterialFrame Make(SKShader under, Rect rect, double radius, double band,
        bool refract, double blur, double tint, bool dark, bool feather = false)
    {
        var profile = new Profile((float)rect.Width, (float)rect.Height,
            (float)Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2), (float)band, refract, (float)blur, feather);
        if (!Cache.TryGetValue(profile, out var mesh))
        {
            if (Cache.Count >= 96)
            {
                var oldest = Cache.First();
                Cache.Remove(oldest.Key);
                oldest.Value.Dispose();
            }
            Cache.Add(profile, mesh = Build(profile));
        }
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom));
        canvas.Translate((float)rect.X, (float)rect.Y);
        using var aligned = under.WithLocalMatrix(SKMatrix.CreateTranslation((float)-rect.X, (float)-rect.Y));
        using var paint = new SKPaint { Shader = aligned, FilterQuality = SKFilterQuality.Low, IsAntialias = true };
        using var tintFilter = Tint(tint, dark);
        paint.ColorFilter = tintFilter;
        canvas.DrawVertices(mesh.Body, SKBlendMode.Modulate, paint);
        if (profile.Band > 0)
        {
            canvas.DrawVertices(mesh.Cool, SKBlendMode.Modulate, paint);
            canvas.DrawVertices(mesh.Warm, SKBlendMode.Modulate, paint);
            paint.ColorFilter = null;
            paint.Shader = null;
            paint.Color = SKColors.White;
            canvas.DrawVertices(mesh.Rim, SKBlendMode.Modulate, paint);
        }
        return new MaterialFrame(recorder.EndRecording());
    }

    internal static SKColorFilter Tint(double tint, bool dark)
    {
        var color = dark ? new SKColor(13, 20, 28) : new SKColor(245, 250, 252);
        return SKColorFilter.CreateBlendMode(color.WithAlpha((byte)(255 * tint)), SKBlendMode.SrcATop);
    }

    // The center has constant displacement. Spend vertices only where the normal or
    // feather changes; the spacing near a straight edge also resolves the thin highlight.
    private static float[] Axis(float length, float curved)
    {
        var points = new SortedSet<float> { 0, length / 2, length };
        var edge = Math.Min(length / 2, curved + 2);
        var step = Math.Max(2, edge / 70);
        for (float d = 1; d < edge; d += step) { points.Add(d); points.Add(length - d); }
        points.Add(edge);
        points.Add(length - edge);
        return points.ToArray();
    }

    private static Mesh Build(Profile p)
    {
        var xs = Axis(p.Width, p.Radius + p.Band);
        var ys = Axis(p.Height, p.Radius + p.Band);
        var count = xs.Length * ys.Length;
        var positions = new SKPoint[count];
        var uv = new SKPoint[count];
        var cool = new SKPoint[count];
        var warm = new SKPoint[count];
        var body = new SKColor[count];
        var coolColor = new SKColor[count];
        var warmColor = new SKColor[count];
        var rim = new SKColor[count];
        var indices = new List<ushort>();
        for (int y = 0; y < ys.Length; y++)
            for (int x = 0; x < xs.Length; x++)
            {
                int i = y * xs.Length + x;
                var pos = positions[i] = new SKPoint(xs[x], ys[y]);
                var dx = pos.X - p.Width / 2;
                var dy = pos.Y - p.Height / 2;
                var qx = Math.Abs(dx) - p.Width / 2 + p.Radius;
                var qy = Math.Abs(dy) - p.Height / 2 + p.Radius;
                float nx = Math.Max(qx, 0), ny = Math.Max(qy, 0);
                var length = MathF.Sqrt(nx * nx + ny * ny);
                var depth = p.Radius - length - Math.Min(Math.Max(qx, qy), 0);
                if (length > .001f) { nx /= length; ny /= length; }
                else { nx = qx > qy ? 1 : 0; ny = 1 - nx; }
                nx *= Math.Sign(dx); ny *= Math.Sign(dy);
                var u = Math.Clamp(depth / Math.Max(.001f, p.Band), 0, 1);
                var edge = 4 * u * (1 - u);
                var shift = p.Bend ? .72f * p.Band * edge : 0;
                var spread = p.Blur * edge + shift * .055f;
                uv[i] = new SKPoint(pos.X - nx * shift, pos.Y - ny * shift);
                cool[i] = new SKPoint(uv[i].X + nx * spread, uv[i].Y + ny * spread);
                warm[i] = new SKPoint(uv[i].X - nx * spread, uv[i].Y - ny * spread);
                float alpha = p.Feather && p.Band > 0 ? u * u * (3 - 2 * u) : Math.Clamp(depth + .5f, 0, 1);
                body[i] = SKColors.White.WithAlpha((byte)(255 * alpha));
                var softAlpha = (byte)(62 * alpha * edge);
                coolColor[i] = new SKColor(239, 251, 255, softAlpha);
                warmColor[i] = new SKColor(255, 248, 242, softAlpha);
                var light = -(nx * .64f + ny * .76f);
                rim[i] = (light >= 0 ? SKColors.White : SKColors.Black).WithAlpha((byte)(
                    MathF.Exp(-Math.Abs(depth - 1) * .78f) * Math.Abs(light) * 138 * alpha));
                if (x + 1 == xs.Length || y + 1 == ys.Length) continue;
                var below = i + xs.Length;
                indices.Add((ushort)i); indices.Add((ushort)(i + 1)); indices.Add((ushort)below);
                indices.Add((ushort)(i + 1)); indices.Add((ushort)(below + 1)); indices.Add((ushort)below);
            }
        var triangles = indices.ToArray();
        SKVertices Layer(SKPoint[] coords, SKColor[] colors) =>
            SKVertices.CreateCopy(SKVertexMode.Triangles, positions, coords, colors, triangles);
        return new Mesh(Layer(uv, body), Layer(cool, coolColor), Layer(warm, warmColor), Layer(uv, rim));
    }
}
