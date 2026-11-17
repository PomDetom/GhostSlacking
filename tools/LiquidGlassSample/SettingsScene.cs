using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using GhostSlacking.App;
using SkiaSharp;

namespace LiquidGlassSample;

internal sealed class SettingsScene : IDisposable
{
    private readonly Fixture _fixture;
    private readonly NavigationView _navigation;
    private readonly Grid _page;
    private readonly ShaderView _shell;
    private readonly List<ShaderView> _surfaces = [];
    private readonly HashSet<Border> _cards = [];
    private readonly SettingsWindow _window;
    private object? _lastPage;
    private SettingsAdjustmentFlyout? _flyout;
    private SKImage? _pagePixels;
    private ShaderView? _flyoutGlass;
    private ShaderView? _previewGlass;
    private TimeSpan _sampledAt;

    public SettingsScene(SettingsWindow window, Fixture fixture)
    {
        _fixture = fixture;
        _window = window;
        _navigation = (NavigationView)window.Content!;
        window.Content = null;
        _shell = new ShaderView(Shell) { IsHitTestVisible = false };
        _surfaces.Add(_shell);
        _page = new Grid { Children = { _shell, _navigation } };
        window.Content = _page;
        // These are window-owned, shared palette brushes. Opacity survives theme changes.
        ((SolidColorBrush)_navigation.Background!).Opacity = 0;
        ((SolidColorBrush)_navigation.Resources["NavigationViewContentBackground"]!).Opacity = 0;
    }

    private SKShader ShellPixels(ShaderView view)
    {
        // All page cards are inset past the shell's curved band. At that depth the
        // shell samples its source at zero displacement and applies only its tint.
        using var source = _fixture.Background(view);
        using var tint = GlassMaterial.Tint(_fixture.Dark ? .66 : .52, _fixture.Dark);
        return source.WithColorFilter(tint);
    }

    private MaterialFrame Shell(ShaderView local)
    {
        using var pixels = _fixture.Background(local);
        return GlassMaterial.Make(pixels, Fixture.RelativeRect(_page, local), 13, 20,
            _fixture.Refract, 2, _fixture.Dark ? .66 : .52, _fixture.Dark);
    }

    public void Refresh(TimeSpan now)
    {
        if (!ReferenceEquals(_lastPage, _navigation.SelectedItem))
        {
            _lastPage = _navigation.SelectedItem;
            _window.UpdateLayout();
            var palette = AppTheme.Palette(_window.ActualThemeVariant);
            foreach (var card in _navigation.GetVisualDescendants().OfType<Border>().ToArray())
            {
                if (card.Background is not SolidColorBrush brush || brush.Color != palette.Surface ||
                    card.CornerRadius.TopLeft < 8 || card.Child == null || !_cards.Add(card)) continue;
                var glass = new ShaderView(v =>
                {
                    using var pixels = ShellPixels(v);
                    return GlassMaterial.Make(pixels, new Rect(v.Bounds.Size), card.CornerRadius.TopLeft,
                        9, _fixture.Refract, 1.6, .21, _fixture.Dark);
                })
                { IsHitTestVisible = false };
                InsertMaterial(card, glass);
                _surfaces.Add(glass);
            }
        }
        foreach (var view in _surfaces)
            if (view.IsEffectivelyVisible && view.IsAttachedToVisualTree()) view.InvalidateVisual();

        var active = _window.ActiveAdjustment;
        if (!ReferenceEquals(active, _flyout))
        {
            ReleasePage();
            if (active?.Presenter is FlyoutPresenter && active.View is Grid grid)
            {
                _flyout = active;
                _flyoutGlass = new ShaderView(v =>
                {
                    using var pixels = PagePixels(v);
                    return GlassMaterial.Make(pixels, new Rect(v.Bounds.Size), 18, 16,
                        _fixture.Refract, 2.3, .66, _fixture.Dark);
                })
                { Margin = new Thickness(-20), IsHitTestVisible = false };
                Grid.SetRowSpan(_flyoutGlass, 3);
                grid.Children.Insert(0, _flyoutGlass);
                if (active.NotificationSample is { } preview)
                {
                    _previewGlass = HudGlass(_fixture, preview.Surface, PagePixels);
                    InsertMaterial(preview.Surface, _previewGlass);
                }
            }
        }
        if (_flyout?.Presenter is FlyoutPresenter presenter)
        {
            presenter.Background = Brushes.Transparent;
            if (_flyout.NotificationSample is { } preview) preview.Surface.Background = Brushes.Transparent;
            if (_pagePixels == null || now - _sampledAt >= TimeSpan.FromMilliseconds(125))
            {
                var scale = _window.RenderScaling;
                var size = PixelSize.FromSize(_page.Bounds.Size, scale);
                if (size.Width > 0 && size.Height > 0)
                {
                    using var frame = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
                    frame.Render(_page);
                    using var buffer = new SKBitmap(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                    frame.CopyPixels(default, buffer.GetPixels(), buffer.ByteCount, buffer.RowBytes);
                    _pagePixels?.Dispose();
                    _pagePixels = SKImage.FromBitmap(buffer);
                    _sampledAt = now;
                }
            }
            _flyoutGlass?.InvalidateVisual();
            _previewGlass?.InvalidateVisual();
        }
    }

    private SKShader PagePixels(ShaderView v)
    {
        if (_pagePixels == null) return _fixture.Background(v);
        var rect = Fixture.RelativeRect(_page, v);
        var matrix = SKMatrix.CreateScaleTranslation((float)rect.Width / _pagePixels.Width,
            (float)rect.Height / _pagePixels.Height, (float)rect.X, (float)rect.Y);
        return _pagePixels.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, matrix);
    }

    public static ShaderView HudGlass(Fixture fixture, Border surface, Func<ShaderView, SKShader> source) =>
        new(v =>
        {
            using var pixels = source(v);
            return GlassMaterial.Make(pixels, new Rect(v.Bounds.Size), surface.CornerRadius.TopLeft,
                Math.Min(9, v.Bounds.Height / 3), fixture.Refract, 1.4, .52, fixture.Dark);
        })
        { IsHitTestVisible = false };

    public static void InsertMaterial(Border border, ShaderView material)
    {
        var content = border.Child!;
        border.Child = null;
        var padding = border.Padding;
        border.Padding = default;
        border.Background = Brushes.Transparent;
        border.Child = new Grid { Children = { material, new Border { Padding = padding, Child = content } } };
    }

    private void ReleasePage()
    {
        _flyout = null;
        _flyoutGlass = _previewGlass = null;
        _pagePixels?.Dispose();
        _pagePixels = null;
    }

    public void Dispose()
    {
        ReleasePage();
    }
}
