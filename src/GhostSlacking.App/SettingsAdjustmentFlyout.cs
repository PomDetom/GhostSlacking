using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App;

internal sealed class SettingsAdjustmentFlyout : Flyout, IDisposable
{
    private readonly AppSettings _initial;
    private readonly AdjustmentKind _kind;
    private readonly Action<AppSettings> _accept;
    private readonly StackPanel _body = new() { Spacing = 12 };
    private readonly TextBlock _caption = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _scaleText = new() { FontSize = 12 };
    private readonly TextBlock _fpsText = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _timer = new();
    private readonly Stopwatch _clock = new();
    private readonly IDisplayRefreshRateProvider _displays;
    private readonly Button? _play;
    private readonly ComboBox? _choice;
    private readonly NotificationContent? _miniature;
    private readonly Canvas? _screen;
    private Window? _owner;
    private FlyoutPresenter? _presenter;
    private bool _completed;
    private bool _disposed;
    private bool _playing;
    private int _stepDirection = 1;
    private DateTime _lastDisplayCheck;
    private double _selection;
    internal SliderNumberEditor? NumberEditor { get; }
    internal Slider? FrameRateSlider { get; }
    internal RevealPreviewControl? RevealSample { get; }
    internal NotificationContent? NotificationSample { get; }
    internal ComboBox? MessageChoice { get; }
    internal Button CompleteButton { get; }
    internal Control? Presenter => _presenter;
    internal Control View => (Control)Content!;
    internal AdjustmentKind Kind => _kind;
    internal bool IsAnimationRunning => _timer.IsEnabled;
    internal RevealFrameRateDecision Decision { get; private set; }
    internal AppSettings Draft => SettingAdjustment.Apply(_initial, _kind, NumberEditor?.Value ?? _selection);

    internal SettingsAdjustmentFlyout(AppSettings initial, AdjustmentKind kind, Action<AppSettings> accept,
        IDisplayRefreshRateProvider? displays = null)
    {
        _initial = initial.Normalize();
        _kind = kind;
        _accept = accept;
        _displays = displays ?? new Win32DisplayRefreshRateProvider();
        var spec = SettingAdjustment.For(kind);
        _selection = SettingAdjustment.Read(_initial, kind);
        Placement = PlacementMode.BottomEdgeAlignedRight;
        VerticalOffset = 6;
        OverlayDismissEventPassThrough = true;
        var close = new Button { Content = "×", Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(close, T("adjustClose"));
        close.Click += (_, _) => Hide();
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(new StackPanel
        {
            Spacing = 2, Children =
            {
                new TextBlock { Text = T(spec.Label), FontSize = 16, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = T("adjustPreview"), FontSize = 12, Opacity = 0.65 }
            }
        });
        Grid.SetColumn(close, 1);
        heading.Children.Add(close);
        _body.Children.Add(heading);
        if (SettingAdjustment.IsNotification(kind))
        {
            NotificationSample = new NotificationContent { HorizontalAlignment = HorizontalAlignment.Center };
            if (kind is AdjustmentKind.NotificationHorizontal or AdjustmentKind.NotificationVertical)
            {
                _miniature = new NotificationContent();
                _screen = new Canvas { Height = 170, ClipToBounds = true, Background = new SolidColorBrush(Color.FromArgb(20, 28, 178, 165)) };
                _screen.Children.Add(_miniature);
                _screen.SizeChanged += (_, _) => RefreshPreview();
                _body.Children.Add(Note(T("adjustScreenPosition")));
                _body.Children.Add(_screen);
            }
            else
            {
                _body.Children.Add(Note(T("adjustActualSize")));
                _body.Children.Add(new Border
                {
                    MinHeight = 140, CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.FromArgb(20, 28, 178, 165)), Child = NotificationSample
                });
                MessageChoice = new ComboBox
                {
                    ItemsSource = new[] { T("adjustShortText"), T("adjustLongText") },
                    SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch
                };
                AutomationProperties.SetName(MessageChoice, T("adjustSample"));
                MessageChoice.SelectionChanged += (_, _) => RefreshPreview();
                _body.Children.Add(MessageChoice);
                NotificationSample.ContentMetricsChanged += (_, _) => RefreshPreview();
            }
        }
        else
        {
            RevealSample = new RevealPreviewControl();
            _body.Children.Add(RevealSample);
            _body.Children.Add(_scaleText);
            RevealSample.SizeChanged += (_, _) => UpdateScale();
            if (kind == AdjustmentKind.DiameterStep)
            {
                var decrease = new Button { Content = T("adjustDecrease") };
                var increase = new Button { Content = T("adjustIncrease") };
                decrease.Click += (_, _) => DemonstrateStep(-1);
                increase.Click += (_, _) => DemonstrateStep(1);
                _body.Children.Add(new WrapPanel { Children = { decrease, increase } });
            }
        }
        _body.Children.Add(_caption);

        if (kind is AdjustmentKind.BlurLevel or AdjustmentKind.Shape)
        {
            var labels = kind == AdjustmentKind.Shape ? new[] { "circle", "rectangle", "roundedRectangle" } : ["blurLow", "blurMedium", "blurHigh"];
            _choice = new ComboBox { ItemsSource = labels.Select(T).ToArray(), SelectedIndex = (int)_selection, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(_choice, T(spec.Label));
            _choice.SelectionChanged += (_, _) => { _selection = _choice.SelectedIndex; RefreshPreview(); };
            _body.Children.Add(_choice);
        }
        else if (kind == AdjustmentKind.FrameRate)
        {
            FrameRateSlider = new Slider { Minimum = 0, Maximum = 3, Value = _selection, SmallChange = 1, LargeChange = 1, TickFrequency = 1, IsSnapToTickEnabled = true, Height = 28, MinHeight = 28 };
            AutomationProperties.SetName(FrameRateSlider, T(spec.Label));
            FrameRateSlider.PropertyChanged += (_, args) =>
            {
                if (args.Property == RangeBase.ValueProperty) { _selection = Math.Round(FrameRateSlider.Value); RefreshPreview(); }
            };
            _body.Children.Add(FrameRateSlider);
            _body.Children.Add(Note($"{T("peekFrameRateAuto")} · 60 · 90 · 120 FPS"));
            _body.Children.Add(_fpsText);
            _play = new Button { Content = T("adjustPlay") };
            _play.Click += (_, _) => SetPlaying(!_playing);
            _body.Children.Add(_play);
        }
        else
        {
            NumberEditor = new SliderNumberEditor(T("adjustCurrentValue"), spec.Unit, _selection,
                spec.Minimum, spec.Maximum, spec.Step, SettingAdjustment.Read(new AppSettings(), kind), T("resetSetting"));
            NumberEditor.ValueChanged += (_, _) => RefreshPreview();
            _body.Children.Add(NumberEditor);
        }
        if (NumberEditor is null)
        {
            var reset = new Button { Content = T("resetSetting") };
            reset.Click += (_, _) =>
            {
                _selection = SettingAdjustment.Read(new AppSettings(), kind);
                if (_choice is not null) _choice.SelectedIndex = (int)_selection;
                if (FrameRateSlider is not null) FrameRateSlider.Value = _selection;
                RefreshPreview();
            };
            _body.Children.Add(reset);
        }
        var cancel = new Button { Content = T("cancel") };
        cancel.Click += (_, _) => Hide();
        CompleteButton = new Button { Content = T("adjustComplete") };
        CompleteButton.Classes.Add("accent");
        CompleteButton.Click += (_, _) => Complete();
        _body.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(AppTheme.Palette(null).Border), Margin = new Thickness(0, 4) });
        _body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, CompleteButton }
        });
        Content = new ScrollViewer { Content = _body, Width = 332, MaxHeight = 570, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        View.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.Escape) { args.Handled = true; Hide(); }
        }, RoutingStrategies.Tunnel);
        View.ActualThemeVariantChanged += (_, _) => ApplyPalette();
        _timer.Tick += OnTick;
        Opened += (_, _) => RefreshPreview();
        Closed += (_, _) => Dispose();
        RefreshPreview();
    }

    protected override Control CreatePresenter()
    {
        _presenter = (FlyoutPresenter)base.CreatePresenter();
        _presenter.CornerRadius = new CornerRadius(18);
        _presenter.Padding = new Thickness(20);
        ApplyPalette();
        return _presenter;
    }

    internal void OpenAt(Control anchor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Let the trigger toggle this flyout without light-dismiss consuming its click.
        OverlayInputPassThroughElement = anchor;
        _owner = TopLevel.GetTopLevel(anchor) as Window;
        if (_owner is not null)
        {
            View.Width = Math.Clamp(_owner.Bounds.Width - 100, 220, 332);
            View.MaxHeight = Math.Max(220, _owner.Bounds.Height - 88);
            _owner.Closed += OnOwnerClosed;
            _owner.PropertyChanged += OnOwnerPropertyChanged;
            _owner.PositionChanged += OnOwnerPositionChanged;
            _owner.ScalingChanged += OnOwnerScalingChanged;
        }
        ShowAt(anchor);
    }

    internal void Complete()
    {
        if (_completed || _disposed) return;
        CompleteButton.Focus();
        var accepted = Draft;
        _completed = true;
        Hide();
        _accept(accepted);
    }

    internal void DemonstrateStep(int direction) { _stepDirection = Math.Sign(direction); RefreshPreview(); }

    private void RefreshPreview()
    {
        if (_disposed) return;
        var draft = Draft;
        if (RevealSample is not null)
        {
            RevealSample.Configure(_initial, draft, _kind, _stepDirection);
            var comparison = RevealSample.Comparison;
            _caption.Text = _kind switch
            {
                AdjustmentKind.DiameterStep => $"{T("adjustBeforeSolid")}: {comparison.BeforeDiameter} px · {T("adjustAfterGhost")}: {comparison.AfterDiameter} px ({comparison.AfterDiameter - comparison.BeforeDiameter:+0;-0;0} px)",
                AdjustmentKind.SoftEdgeWidth => $"{T("adjustHardEdgeSolid")} · {T("adjustFeatherGhost")}: {draft.RevealSoftEdgeWidthPx} px",
                _ => T("adjustRevealHint")
            };
            UpdateScale();
            if (_kind == AdjustmentKind.FrameRate) RefreshFrameRate();
        }
        if (NotificationSample is not null)
        {
            var message = T(MessageChoice?.SelectedIndex == 1 ? "adjustLongMessage" : "notificationPreviewMessage");
            var hud = draft.NotificationHud;
            var scaling = _owner?.RenderScaling ?? 1;
            NotificationSample.Update(message, UserNotificationSeverity.Info, false);
            NotificationSample.MeasurePreferredWidth(hud, scaling);
            NotificationSample.Configure(hud);
            if (_miniature is not null && _screen is not null)
            {
                var screen = _owner?.Screens.ScreenFromWindow(_owner) ?? _owner?.Screens.Primary;
                var work = screen?.WorkingArea ?? new PixelRect(0, 0, 1440, 900);
                scaling = screen?.Scaling ?? 1;
                _miniature.Update(message, UserNotificationSeverity.Info, false);
                var geometry = NotificationLayout.Calculate(work, scaling, hud, _miniature.MeasurePreferredWidth(hud, scaling));
                _miniature.Configure(hud, geometry);
                var scale = Math.Min(Math.Max(1, _screen.Bounds.Width) / work.Width, _screen.Height / work.Height);
                _miniature.RenderTransformOrigin = RelativePoint.TopLeft;
                _miniature.RenderTransform = new ScaleTransform(scale * scaling, scale * scaling);
                Canvas.SetLeft(_miniature, (_screen.Bounds.Width - work.Width * scale) / 2 + (geometry.Position.X - work.X) * scale);
                Canvas.SetTop(_miniature, (_screen.Height - work.Height * scale) / 2 + (geometry.Position.Y - work.Y) * scale);
            }
            _caption.Text = T(_screen is null ? "adjustWidthHint" : "adjustScreenHint");
        }
    }

    private void RefreshFrameRate()
    {
        var position = _owner?.PointToScreen(new Point(_owner.Bounds.Width / 2, _owner.Bounds.Height / 2)) ?? new PixelPoint();
        Decision = RevealFrameRatePolicy.Create(true, Draft.PeekFrameRateLimit, _displays.GetForPoint(new System.Drawing.Point(position.X, position.Y)));
        _timer.Interval = Decision.Interval;
        _fpsText.Text = $"{SettingAdjustment.Format(Draft, _kind, _initial.Language)} → {Decision.TargetFramesPerSecond} FPS" +
            (Decision.UsesFallback ? $" · {T("adjustFpsFallback")}" : $" · {Decision.DetectedRefreshRate} Hz");
        _lastDisplayCheck = DateTime.UtcNow;
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (DateTime.UtcNow - _lastDisplayCheck > TimeSpan.FromSeconds(1)) RefreshFrameRate();
        RevealSample!.AnimationSeconds = _clock.Elapsed.TotalSeconds;
        RevealSample.InvalidateVisual();
    }

    internal void SetPlaying(bool playing)
    {
        _playing = playing;
        if (_play is not null) _play.Content = T(playing ? "adjustPause" : "adjustPlay");
        if (playing && IsOpen && !_disposed) { _clock.Start(); _timer.Start(); }
        else { _clock.Stop(); _timer.Stop(); }
    }

    private void UpdateScale() => _scaleText.Text = $"{T("adjustSample")} · {T("adjustScale")}: {RevealSample?.PreviewScale:P0}";
    private string T(string key) => UiText.Text(_initial.Language, key);
    private static TextBlock Note(string text) => new() { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 };
    private void ApplyPalette()
    {
        if (_presenter is null) return;
        var palette = AppTheme.Palette(View.ActualThemeVariant);
        _presenter.Background = new SolidColorBrush(palette.Surface);
        _presenter.Foreground = new SolidColorBrush(palette.Primary);
        _presenter.BorderBrush = new SolidColorBrush(palette.Border);
        _caption.Foreground = new SolidColorBrush(palette.Secondary);
    }
    private void OnOwnerClosed(object? sender, EventArgs args) => Hide();
    private void OnOwnerPositionChanged(object? sender, PixelPointEventArgs args) => Hide();
    private void OnOwnerScalingChanged(object? sender, EventArgs args) => Hide();
    private void OnOwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Window.WindowStateProperty && _owner?.WindowState == WindowState.Minimized) Hide();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _clock.Stop();
        RevealSample?.Dispose();
        if (_owner is not null)
        {
            _owner.Closed -= OnOwnerClosed;
            _owner.PropertyChanged -= OnOwnerPropertyChanged;
            _owner.PositionChanged -= OnOwnerPositionChanged;
            _owner.ScalingChanged -= OnOwnerScalingChanged;
        }
    }
}
