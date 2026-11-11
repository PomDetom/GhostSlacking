using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App;

internal sealed class RevealAdjustmentWindow : SettingsAdjustmentWindow
{
    internal readonly SliderNumberEditor Diameter;
    internal readonly SliderNumberEditor DiameterStep;
    internal readonly SliderNumberEditor SoftEdge;
    internal readonly Slider FrameRate;
    internal readonly RevealPreviewControl Sample = new();
    private readonly ComboBox _shape;
    private readonly ComboBox _blur;
    private readonly TextBlock _fpsText = new();
    private readonly TextBlock _scaleText = new();
    private readonly TextBlock _stepText = new();
    private readonly Button _play;
    private readonly DispatcherTimer _timer = new();
    private readonly Stopwatch _clock = new();
    private readonly IDisplayRefreshRateProvider _displays;
    private DateTime _lastDisplayCheck;
    private bool _playing = true;
    private bool _opened;
    private bool _closed;
    private int _demoDiameter;
    internal int DemoDiameter => _demoDiameter;
    internal bool IsAnimationRunning => _timer.IsEnabled;
    internal RevealFrameRateDecision Decision { get; private set; }

    internal RevealAdjustmentWindow(AppSettings initial, IDisplayRefreshRateProvider? displays = null)
        : base(initial, "adjustRevealTitle")
    {
        _displays = displays ?? new Win32DisplayRefreshRateProvider();
        Diameter = AddNumber("diameter", "px", Initial.RevealDiameterPx, 64, 800, 8, 144);
        DiameterStep = AddNumber("diameterStep", "px", Initial.RevealDiameterStepPx, 8, 256, 8, 16);
        SoftEdge = AddNumber("softEdgeWidth", "px", Initial.RevealSoftEdgeWidthPx, 0, 128, 4, 16);
        _shape = AddChoice("shape", [T("circle"), T("rectangle"), T("roundedRectangle")], (int)Initial.RevealShape, (int)new AppSettings().RevealShape);
        _blur = AddChoice("blurLevel", [T("blurLow"), T("blurMedium"), T("blurHigh")], (int)Initial.RevealBlurLevel, (int)new AppSettings().RevealBlurLevel);
        FrameRate = new Slider { Height = 28, MinHeight = 28, Minimum = 0, Maximum = 3, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 1, Value = FrameIndex(Initial.PeekFrameRateLimit) };
        AutomationProperties.SetName(FrameRate, T("peekFrameRateLimit"));
        var resetFps = new Button { Content = T("resetSetting"), HorizontalAlignment = HorizontalAlignment.Right };
        resetFps.Click += (_, _) => FrameRate.Value = 0;
        Editors.Children.Add(new StackPanel
        {
            Spacing = 6, Children = { new TextBlock { Text = T("peekFrameRateLimit") }, FrameRate,
                Note($"{T("peekFrameRateAuto")} · 60 · 90 · 120 FPS"), _fpsText, resetFps }
        });
        Preview.Children.Add(new TextBlock { Text = T("adjustSample"), FontWeight = Avalonia.Media.FontWeight.SemiBold });
        Preview.Children.Add(Sample);
        Preview.Children.Add(_scaleText);
        Preview.Children.Add(Note(T("adjustRevealHint")));
        var decrease = new Button { Content = T("adjustDecrease") };
        var increase = new Button { Content = T("adjustIncrease") };
        decrease.Click += (_, _) => DemonstrateStep(-1);
        increase.Click += (_, _) => DemonstrateStep(1);
        Preview.Children.Add(new WrapPanel { Orientation = Orientation.Horizontal, Children = { decrease, increase } });
        Preview.Children.Add(_stepText);
        _play = new Button { Content = T("adjustPause") };
        _play.Click += (_, _) => SetPlaying(!_playing);
        Preview.Children.Add(_play);
        Diameter.ValueChanged += (_, _) => { _demoDiameter = (int)Diameter.Value; RefreshPreview(); };
        DiameterStep.ValueChanged += (_, _) => RefreshPreview();
        SoftEdge.ValueChanged += (_, _) => RefreshPreview();
        _shape.SelectionChanged += (_, _) => RefreshPreview();
        _blur.SelectionChanged += (_, _) => RefreshPreview();
        FrameRate.PropertyChanged += (_, args) =>
        {
            if (args.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) RefreshPreview();
        };
        Sample.SizeChanged += (_, _) => UpdateScale();
        _timer.Tick += OnTick;
        Opened += (_, _) => { _opened = true; RefreshPreview(); UpdateAnimation(); };
        PositionChanged += (_, _) => RefreshFrameRate();
        PropertyChanged += (_, args) => { if (args.Property == WindowStateProperty) UpdateAnimation(); };
        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
            _clock.Stop();
            Sample.Dispose();
        };
        _demoDiameter = Initial.RevealDiameterPx;
        RefreshPreview();
    }

    private ComboBox AddChoice(string label, string[] choices, int index, int fallback)
    {
        var combo = new ComboBox { ItemsSource = choices, SelectedIndex = index, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(combo, T(label));
        var reset = new Button { Content = "↺" };
        AutomationProperties.SetName(reset, T("resetSetting") + ": " + T(label));
        reset.Click += (_, _) => combo.SelectedIndex = fallback;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(combo);
        Grid.SetColumn(reset, 1);
        row.Children.Add(reset);
        Editors.Children.Add(new StackPanel { Spacing = 6, Children = { new TextBlock { Text = T(label) }, row } });
        return combo;
    }

    internal static int FrameIndex(PeekFrameRateLimit value) => value switch
    { PeekFrameRateLimit.Fps60 => 1, PeekFrameRateLimit.Fps90 => 2, PeekFrameRateLimit.Fps120 => 3, _ => 0 };

    internal static PeekFrameRateLimit FrameValue(int index) => index switch
    { 1 => PeekFrameRateLimit.Fps60, 2 => PeekFrameRateLimit.Fps90, 3 => PeekFrameRateLimit.Fps120, _ => PeekFrameRateLimit.Auto };

    protected override AppSettings ReadDraft() => Initial with
    {
        RevealDiameterPx = (int)Diameter.Value,
        RevealDiameterStepPx = (int)DiameterStep.Value,
        RevealSoftEdgeWidthPx = (int)SoftEdge.Value,
        RevealShape = (RevealShape)_shape.SelectedIndex,
        RevealBlurLevel = (RevealBlurLevel)_blur.SelectedIndex,
        PeekFrameRateLimit = FrameValue((int)Math.Round(FrameRate.Value))
    };

    internal void DemonstrateStep(int direction)
    {
        var previous = _demoDiameter;
        _demoDiameter = Math.Clamp(previous + Math.Sign(direction) * (int)DiameterStep.Value, 64, 800);
        _stepText.Text = $"{previous} → {_demoDiameter} px";
        Sample.Update(ReadDraft(), _demoDiameter);
    }

    private void RefreshPreview()
    {
        Sample.Update(ReadDraft(), _demoDiameter);
        _stepText.Text = $"{_demoDiameter} px · ±{(int)DiameterStep.Value} px";
        RefreshFrameRate();
        UpdateScale();
    }

    private void UpdateScale() => _scaleText.Text = $"{T("adjustScale")}: {Sample.PreviewScale:P0} · {_demoDiameter} px";

    private void RefreshFrameRate()
    {
        var point = new System.Drawing.Point(Position.X + (int)(Bounds.Width * RenderScaling / 2), Position.Y + (int)(Bounds.Height * RenderScaling / 2));
        var selected = FrameValue((int)Math.Round(FrameRate.Value));
        Decision = RevealFrameRatePolicy.Create(true, selected, _displays.GetForPoint(point));
        var label = selected == PeekFrameRateLimit.Auto ? T("peekFrameRateAuto") : $"{(FrameIndex(selected) + 1) * 30} FPS";
        _fpsText.Text = $"{label} → {Decision.TargetFramesPerSecond} FPS" + (Decision.UsesFallback ? $" · {T("adjustFpsFallback")}" : $" · {Decision.DetectedRefreshRate} Hz");
        _fpsText.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _timer.Interval = Decision.Interval;
        _lastDisplayCheck = DateTime.UtcNow;
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (DateTime.UtcNow - _lastDisplayCheck > TimeSpan.FromSeconds(1)) RefreshFrameRate();
        Sample.AnimationSeconds = _clock.Elapsed.TotalSeconds;
        Sample.InvalidateVisual();
    }

    internal void SetPlaying(bool playing)
    {
        _playing = playing;
        _play.Content = T(playing ? "adjustPause" : "adjustPlay");
        UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        if (_playing && _opened && !_closed && WindowState != WindowState.Minimized)
        { _clock.Start(); _timer.Start(); }
        else { _clock.Stop(); _timer.Stop(); }
    }
}
