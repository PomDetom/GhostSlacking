using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using GhostSlacking.App;
using GhostSlacking.Core;
using GhostSlacking.Platform;
using SkiaSharp;

namespace LiquidGlassSample;

internal sealed partial class SampleWindow : Window
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Win32WindowApi _windows = new();
    private readonly Fixture _fixture;
    private readonly ShaderView _stage;
    private readonly TextBlock _status = new() { Opacity = .78, FontSize = 12 };
    private readonly NotificationWindow _hud = new();
    private readonly ShaderView _hudGlass;
    private readonly Window _lens;
    private readonly ShaderView _lensGlass;
    private readonly Button _pause;
    private readonly Button _refraction;
    private readonly FrameMeter _meter = new();
    private readonly Win32DisplayRefreshRateProvider _displays = new();
    private TimeSpan _nextFrame, _nextReadout;
    private SettingsWindow? _settings;
    private SettingsScene? _settingsScene;
    private AppSettings _saved = new() { ThemeMode = UiThemeMode.Light };
    private TimeSpan _lastFrame, _hudRemaining, _hudLastTick;
    private bool _paused, _peeking, _clickable, _closing;
    private bool _lensFailed;
    private (nint Hwnd, CircleRegion Core, int Feather)? _appliedRing;
    private Point? _pointer;
    private int _clicks;
    private string _performance = "";
    private string _feedback = "";

    public SampleWindow(string[] args)
    {
        Title = "GhostSlacking · 液态玻璃样板";
        Width = 1200;
        Height = 840;
        WindowState = WindowState.Maximized;
        _stage = new ShaderView(StageShader);
        _fixture = new Fixture { Stage = _stage };
        _lensGlass = new ShaderView(LensShader, error => Dispatcher.UIThread.Post(() => HardEdge(error.Message)),
            _meter.Record);
        _lens = new Window
        {
            Title = "Reveal · 羽化外环",
            CanResize = false,
            SystemDecorations = SystemDecorations.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Content = _lensGlass
        };
        _hudGlass = SettingsScene.HudGlass(_fixture, _hud.Hud.Surface, v => _fixture.Background(v));
        Win32Properties.AddWndProcHookCallback(_lens, PassThroughHit);
        Win32Properties.AddWndProcHookCallback(_hud, PassThroughHit);
        SettingsScene.InsertMaterial(_hud.Hud.Surface, _hudGlass);
        _hud.Clicked += (_, _) =>
        {
            if (!_clickable) return;
            _clickable = false;
            _hud.Hide();
            _feedback = "已接收更新通知";
        };
        var tools = new WrapPanel();
        void Add(Button button) => tools.Children.Add(button);
        Add(ActionButton("设置页面", ShowSettings));
        Add(ActionButton("普通通知", () => Notify(false)));
        Add(ActionButton("可点击通知", () => Notify(true)));
        Add(ActionButton("Reveal", StartReveal));
        Add(_pause = ActionButton("暂停背景", TogglePause));
        Add(_refraction = ActionButton("折射 · 开", ToggleRefraction));
        Add(ActionButton("切换明暗", () => SetTheme(_fixture.Dark ? UiThemeMode.Light : UiThemeMode.Dark)));
        var bar = new StackPanel { Margin = new Thickness(16, 9), Spacing = 5, Children = { tools, _status } };
        var stagePanel = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#253848")),
            Child = _stage,
            ClipToBounds = true
        };
        // The shader has no input hit geometry; the fixture receives native pointer events.
        stagePanel.PointerMoved += (_, e) => { _pointer = e.GetPosition(_stage); if (_peeking) MoveLens(); };
        stagePanel.PointerExited += (_, _) => { _pointer = null; _lens.Hide(); UpdateCoreClip(); };
        stagePanel.PointerPressed += (_, _) => { _feedback = $"测试背景收到点击 {++_clicks}"; };
        var layout = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        layout.Children.Add(bar);
        layout.Children.Add(stagePanel);
        Content = layout;
        PositionChanged += (_, _) => { if (_hud.IsVisible) PositionHud(); };
        _stage.SizeChanged += (_, _) => { if (_hud.IsVisible) PositionHud(); };
        Opened += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            ShowSettings();
            RequestAnimationFrame(Frame);
            if (args.Contains("--verify")) await VerifyAndClose();
        });
        Closed += (_, _) =>
        {
            _closing = true;
            _settings?.Close();
            _hud.Close();
            _lens.Close();
            _fixture.Dispose();
        };
    }

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 6, 2),
            Padding = new Thickness(13, 6),
            CornerRadius = new CornerRadius(8)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void TogglePause()
    {
        _paused = !_paused;
        _pause.Content = _paused ? "继续背景" : "暂停背景";
    }

    private void ToggleRefraction()
    {
        _fixture.Refract = !_fixture.Refract;
        _refraction.Content = _fixture.Refract ? "折射 · 开" : "折射 · 关";
    }

    private void SetTheme(UiThemeMode theme)
    {
        AppTheme.Apply(theme);
        _fixture.Dark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
    }

    private void ShowSettings()
    {
        _peeking = false;
        UpdateCoreClip();
        _lens.Hide();
        _hud.Hide();
        if (_settings == null)
        {
            var window = new SettingsWindow(_saved, draft => { _saved = draft; return true; }, SetTheme)
            { Title = "GhostSlacking · 设置样板", WindowStartupLocation = WindowStartupLocation.Manual };
            _settingsScene = new SettingsScene(window, _fixture);
            _settings = window;
            window.Closed += (_, _) =>
            {
                _settingsScene?.Dispose();
                _settingsScene = null;
                _settings = null;
            };
            window.Position = _stage.PointToScreen(new Point(Math.Max(0, (_stage.Bounds.Width - window.Width) / 2),
                Math.Max(0, (_stage.Bounds.Height - window.Height) / 2)));
        }
        if (!_settings.IsVisible) _settings.Show(this);
        _settings.Activate();
        _feedback = "设置保存至本次会话";
    }

    private void StartReveal()
    {
        _settings?.Hide();
        _hud.Hide();
        _peeking = true;
        _lensFailed = false;
        _meter.Reset();
        _pointer = null;
        UpdateCoreClip();
        _feedback = "在网格上移动、点击；尺寸、形状与羽化取自设置页";
    }

    private void Notify(bool clickable)
    {
        _settings?.Hide();
        _peeking = false;
        UpdateCoreClip();
        _lens.Hide();
        var visible = _hud.IsVisible;
        _clickable = clickable;
        _hud.Update(clickable ? "发现新版本" : visible ? "设置已保存" : "已恢复窗口",
            clickable ? UserNotificationSeverity.Info : UserNotificationSeverity.Success, clickable);
        _hud.Hud.Surface.Background = Brushes.Transparent;
        PositionHud();
        if (!visible) _hud.Show(this);
        if (!SetInputMode(_hud, !clickable)) return;
        var raised = _windows.BringToTopWithoutActivation(_hud.TryGetPlatformHandle()!.Handle);
        if (!raised.Success) { _hud.Hide(); _feedback = raised.Operation; return; }
        if (!visible || _hud.IsFadingOut) _hud.BeginFadeIn(DateTimeOffset.UtcNow, reset: !visible);
        _hudRemaining = TimeSpan.FromSeconds(clickable ? 4.5 : 2.5);
        _hudLastTick = _clock.Elapsed;
        _feedback = clickable ? "悬停可暂停，单击可接收" : "普通通知可穿透；再次触发可替换消息";
    }

    private void PositionHud()
    {
        var hud = _saved.NotificationHud;
        var area = new PixelRect(_stage.PointToScreen(default), PixelSize.FromSize(_stage.Bounds.Size, RenderScaling));
        var geometry = NotificationLayout.Calculate(area, RenderScaling, hud, _hud.MeasurePreferredWidth(hud, RenderScaling));
        _hud.Configure(hud, geometry);
        _hud.Position = geometry.Position;
    }

    private bool SetInputMode(Window window, bool through)
    {
        var result = _windows.SetNotificationClickThrough(window.TryGetPlatformHandle()!.Handle, through);
        if (result.Success) return true;
        window.Hide();
        _feedback = $"覆盖窗已收起 · {result.Operation} ({result.ErrorCode})";
        return false;
    }

    private nint PassThroughHit(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        const uint nonClientHitTest = 0x84;
        if (message == nonClientHitTest)
        {
            handled = true;
            // HTCLIENT for the clickable HUD; HTTRANSPARENT continues to the fixture.
            return _clickable && hwnd == _hud.TryGetPlatformHandle()?.Handle ? 1 : -1;
        }
        return 0;
    }

    private RevealVisualState Geometry()
    {
        var core = RevealGeometry.CreateReveal(default, default,
            new RevealSettings { DiameterPx = _saved.RevealDiameterPx, Shape = _saved.RevealShape });
        return RevealGeometry.CreateVisualState(0, default, core, _saved.RevealSoftEdgeWidthPx, _saved.RevealBlurLevel);
    }

    private static double Radius(CircleRegion shape) => shape.Shape == RevealShape.Circle ? shape.Radius : shape.CornerRadius;

    private MaterialFrame StageShader(ShaderView view) => _fixture.Background(view);

    private void UpdateCoreClip()
    {
        if (!_peeking) { _stage.Clip = null; return; }
        if (_pointer is not Point p) { _stage.Clip = new RectangleGeometry(default); return; }
        var core = Geometry().CoreRegion;
        var diameter = core.DiameterPx / RenderScaling;
        var rect = new Rect(p.X - diameter / 2, p.Y - diameter / 2, diameter, diameter);
        _stage.Clip = new RectangleGeometry(rect, Radius(core) / RenderScaling, Radius(core) / RenderScaling);
    }

    private void HardEdge(string reason)
    {
        if (_closing) return;
        _lensFailed = true;
        _lens.Hide();
        _feedback = $"硬边 Reveal · {reason}";
    }

    private MaterialFrame LensShader(ShaderView view)
    {
        using var source = _fixture.Background(view);
        var visual = Geometry();
        var outside = RevealGeometry.Expand(visual.CoreRegion, visual.FeatherWidthPx);
        var scale = _lens.RenderScaling;
        return GlassMaterial.Make(source, new Rect(view.Bounds.Size), Radius(outside) / scale,
            visual.FeatherWidthPx / scale, _fixture.Refract, visual.BlurAmountPx / scale, 0, false, true);
    }

    private void MoveLens()
    {
        if (!_peeking || _pointer is not Point point) return;
        UpdateCoreClip();
        if (_lensFailed) return;
        var diameter = _saved.RevealDiameterPx + 2 * _saved.RevealSoftEdgeWidthPx;
        var side = diameter / RenderScaling;
        _lens.Width = _lens.Height = side;
        _lens.Position = _stage.PointToScreen(new Point(point.X - side / 2, point.Y - side / 2));
        // At the fixture boundary only the clipped hard core is valid.
        if (_saved.RevealSoftEdgeWidthPx == 0 || !new Rect(_stage.Bounds.Size).Contains(
            new Rect(point.X - side / 2, point.Y - side / 2, side, side)))
        {
            _lens.Hide();
            return;
        }
        if (!_lens.IsVisible)
        {
            _lens.Show(this);
            if (!SetInputMode(_lens, true)) { HardEdge(_feedback); return; }
        }
        var hwnd = _lens.TryGetPlatformHandle()!.Handle;
        var core = Geometry().CoreRegion with { CenterX = diameter / 2, CenterY = diameter / 2 };
        var feather = _saved.RevealSoftEdgeWidthPx;
        if (_appliedRing != (hwnd, core, feather))
        {
            var result = Win32OverlayWindow.SetRingRegion(hwnd, RevealGeometry.Expand(core, feather), core);
            if (!result.Success) { HardEdge(result.Operation); return; }
            _appliedRing = (hwnd, core, feather);
        }
    }

    private void Frame(TimeSpan timestamp)
    {
        if (_closing) return;
        var now = _clock.Elapsed;
        if (now < _nextFrame) { RequestAnimationFrame(Frame); return; }
        var location = _stage.PointToScreen(_pointer ?? default);
        var display = _displays.GetForPoint(new System.Drawing.Point(location.X, location.Y));
        var pace = RevealFrameRatePolicy.Create(_peeking, _saved.PeekFrameRateLimit, display);
        _nextFrame += pace.Interval;
        if (_nextFrame <= now) _nextFrame = now + pace.Interval;
        var elapsed = now - _lastFrame;
        _lastFrame = now;
        if (!_paused) _fixture.Phase += elapsed.TotalSeconds * .64;
        _stage.InvalidateVisual();
        if (_settings?.IsVisible == true) _settingsScene?.Refresh(now);
        if (_peeking && _lens.IsVisible) _lensGlass.InvalidateVisual();
        if (_hud.IsVisible)
        {
            _hudGlass.InvalidateVisual();
            if (!_clickable || !_hud.IsPointerOver) _hudRemaining -= now - _hudLastTick;
            _hudLastTick = now;
            if (_hudRemaining <= TimeSpan.Zero && !_hud.IsFadingOut)
                _hud.BeginFadeOut(DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(200));
            _hud.AdvanceFade(DateTimeOffset.UtcNow);
        }
        if (now >= _nextReadout)
        {
            _performance = _peeking ? $"目标 {pace.TargetFramesPerSecond} FPS · {_meter.Read()}" : "";
            _nextReadout = now + TimeSpan.FromSeconds(1);
        }
        var status = $"像素来源：样板自建窗口 / 内存   ·   {_feedback}   ·   {_performance}";
        if (_status.Text != status) _status.Text = status;
        RequestAnimationFrame(Frame);
    }
}
