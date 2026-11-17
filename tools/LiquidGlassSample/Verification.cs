using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using GhostSlacking.App;
using GhostSlacking.Core;
using GhostSlacking.Platform;
using SkiaSharp;

namespace LiquidGlassSample;

internal sealed partial class SampleWindow
{
    private async Task VerifyAndClose()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            await Task.Delay(750);
            TogglePause();
            var settings = _settings!;
            CheckAlignment(settings);
            settings.Position += new PixelPoint(37, 19);
            await Task.Delay(120);
            CheckAlignment(settings);
            var nav = settings.GetVisualDescendants().OfType<NavigationView>().Single();
            var pages = nav.MenuItems.Concat(nav.FooterMenuItems).OfType<NavigationViewItem>().ToArray();
            Require(pages.Length == 6, "Missing settings page");
            foreach (var page in pages)
            {
                nav.SelectedItem = page;
                await Task.Delay(100);
                CheckPixels((Control)settings.Content!, $"Settings/{page.Tag}");
            }
            SetTheme(UiThemeMode.Dark);
            nav.SelectedItem = pages[0];
            await Task.Delay(200);
            settings.OpenAdjustment(AdjustmentKind.SoftEdgeWidth, nav);
            await Task.Delay(450);
            var flyout = settings.ActiveAdjustment!;
            CheckPixels(flyout.Presenter!, "Adjustment");
            flyout.Hide();
            SetTheme(UiThemeMode.Light);
            Notify(false);
            var timeout = _clock.Elapsed + TimeSpan.FromSeconds(3);
            while (_clock.Elapsed < timeout && (_hud.Opacity < .99 || _hudGlass.Bounds.Width <= 0))
                await Task.Delay(16);
            Require(_hud.IsVisible && _hud.Opacity >= .99 && _hudGlass.Bounds.Width > 0,
                $"HUD readiness: {_feedback}");
            CheckPixels(_hud.Hud, "HUD");
            VerifyHit(_hud.Hud.Surface, this, "HUD click-through");
            Notify(true);
            Require(_hud.IsVisible && _clickable, "HUD replacement");
            await Task.Delay(100);
            VerifyHit(_hud.Hud.Surface, _hud, "Clickable HUD");
            StartReveal();
            _pointer = new Point(_stage.Bounds.Width / 2, _stage.Bounds.Height / 2);
            foreach (var shape in Enum.GetValues<RevealShape>())
            {
                _saved = _saved with { RevealShape = shape };
                MoveLens();
                await Task.Delay(150);
                Require(_lens.IsVisible, "Reveal HWND");
                CheckPixels(_lensGlass, $"Reveal/{shape}");
                VerifyHit(_lensGlass, this, "Reveal center hit");
            }
            _saved = _saved with { RevealSoftEdgeWidthPx = 0 };
            MoveLens();
            Require(!_lens.IsVisible, "Zero-feather hard core");
            _saved = _saved with { RevealSoftEdgeWidthPx = 16 };
            MoveLens();
            HardEdge("样板故障验证");
            Require(!_lens.IsVisible && _stage.Clip != null, "Hard-edge fallback");
            StartReveal();
            _pointer = new Point(_stage.Bounds.Width / 2, _stage.Bounds.Height / 2);
            TogglePause();
            var point = _stage.PointToScreen(_pointer.Value);
            var refresh = new Win32DisplayRefreshRateProvider().GetForPoint(new System.Drawing.Point(point.X, point.Y));
            Console.WriteLine($"Windows {Environment.OSVersion.Version}; DPI scale {RenderScaling}; display {refresh?.Hertz} Hz");
            foreach (var cap in new[] { PeekFrameRateLimit.Fps60, PeekFrameRateLimit.Fps120 })
            {
                _saved = _saved with { PeekFrameRateLimit = cap };
                _meter.Reset();
                for (int i = 0; i < 90; i++)
                {
                    _pointer = new Point(_stage.Bounds.Width / 2 + 90 * Math.Sin(i * .1), _stage.Bounds.Height / 2);
                    MoveLens();
                    await Task.Delay(28);
                }
                Console.WriteLine($"{cap}: {_meter.Read()}");
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Program.VerificationFailed = true;
        }
        finally { Close(); }
    }

    private void CheckAlignment(Window settings)
    {
        static SKColor Read(SKShader shader, Point point)
        {
            using var pixel = new SKBitmap(1, 1);
            using var canvas = new SKCanvas(pixel);
            using var paint = new SKPaint { Shader = shader, FilterQuality = SKFilterQuality.Low };
            canvas.Translate(.5f - (float)point.X, .5f - (float)point.Y);
            canvas.DrawPaint(paint);
            return pixel.GetPixel(0, 0);
        }
        var client = (Control)settings.Content!;
        using var background = _fixture.Background(_stage);
        using var sampled = _fixture.Background(client);
        for (int i = 1; i <= 12; i++)
        {
            var screen = client.PointToScreen(new Point(50 + i * 36, 60 + i * 22));
            var a = Read(background, _stage.PointToClient(screen));
            var b = Read(sampled, client.PointToClient(screen));
            Require(Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue) < 12, "Fixture alignment");
        }
        Console.WriteLine($"Fixture alignment passed at {settings.Position}");
    }

    private void VerifyHit(Control surface, Window expected, string label)
    {
        var p = surface.PointToScreen(new Point(surface.Bounds.Width / 2, surface.Bounds.Height / 2));
        var hit = Win32NativeMethods.WindowFromPoint(new Win32NativeMethods.POINT { X = p.X, Y = p.Y });
        var root = Win32NativeMethods.GetAncestor(hit, 2);
        var hud = _hud.TryGetPlatformHandle()!.Handle;
        Win32NativeMethods.GetWindowRect(hud, out var r);
        Require(root == expected.TryGetPlatformHandle()!.Handle,
            $"{label} at {p}; expected {expected.TryGetPlatformHandle()!.Handle:X}, hit {root:X}, " +
            $"fixture {TryGetPlatformHandle()!.Handle:X}, HUD {hud:X} ({r.Left},{r.Top}..{r.Right},{r.Bottom}, " +
            $"visible {_hud.IsVisible}, opacity {_hud.Opacity}, exstyle {Win32NativeMethods.GetWindowLongPtr(hud, -20):X}), lens {_lens.TryGetPlatformHandle()?.Handle:X}");
    }

    private void CheckPixels(Control view, string label)
    {
        SKBitmap Render(bool enabled)
        {
            _fixture.Refract = enabled;
            var size = PixelSize.FromSize(view.Bounds.Size, 1);
            using var target = new RenderTargetBitmap(size);
            target.Render(view);
            var pixels = new SKBitmap(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            target.CopyPixels(default, pixels.GetPixels(), pixels.ByteCount, pixels.RowBytes);
            return pixels;
        }
        using var off = Render(false);
        using var on = Render(true);
        var a = off.GetPixelSpan();
        var b = on.GetPixelSpan();
        var changed = 0;
        for (int i = 0; i < a.Length; i += 4)
            if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) >= 4) changed++;
        Require(changed >= 12, $"{label}: no visible refraction delta ({changed} pixels)");
        Console.WriteLine($"{label}: {changed} changed pixels");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
