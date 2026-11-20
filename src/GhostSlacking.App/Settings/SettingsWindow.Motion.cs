using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;
using Path = Avalonia.Controls.Shapes.Path;

namespace GhostSlacking.App;

internal sealed partial class SettingsWindow
{
    private static readonly Geometry PaneToggleGlyph = Geometry.Parse("M2,3.5 H14 M2,8 H14 M2,12.5 H14");
    private const string PaneLabelOpacity = "GhostSlackingPaneLabelOpacity";
    private static readonly TimeSpan WindowEntranceDuration = TimeSpan.FromMilliseconds(245);
    private readonly StackPanel _pageHeader = new();
    private readonly UiEntrance _pageEntrance = new(245, slideDistance: 12);
    private readonly TranslateTransform _windowEntranceOffset = new();
    private DispatcherOperation? _windowEntranceStart;
    private bool _windowEntrancePrepared;
    private readonly Transitions _paneTransitions =
    [
        new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new CubicEaseInOut() }
    ];
    private readonly Transitions _selectionTransitions =
    [
        new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(290), Easing = new SineEaseInOut() }
    ];
    private readonly TranslateTransform _selectionOffset = new();
    private readonly SolidColorBrush _selectionOutlineBrush = new();
    private readonly Border _selectionBackground = new()
    {
        CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false, IsVisible = false
    };
    private UiMotionSettings? _navigationMotion;
    private SplitView? _navigationSplitView;
    private Panel? _navigationPane;
    private Grid? _paneHeader;
    private TextBlock? _paneTitle;
    private Button? _paneToggle;
    private bool _paneHasLayout;

    private void ConfigureNavigationMotion()
    {
        _selectionBackground.Background = _selectionBrush;
        _selectionBackground.BorderBrush = _selectionOutlineBrush;
        _selectionBackground.RenderTransform = _selectionOffset;
        _navigation.Styles.Add(new Style(selector => selector.OfType<NavigationViewItemPresenter>()
            .Template().OfType<ContentPresenter>().Name("ContentPresenter"))
        {
            Setters = { new Setter(OpacityProperty, new DynamicResourceExtension(PaneLabelOpacity)) }
        });
        UiMotionSettings.Current?.Refresh();
        UpdateNavigationTransitions();
        _navigation.TemplateApplied += OnNavigationTemplateApplied;
        _navigation.LayoutUpdated += OnNavigationLayoutUpdated;
        Opened += OnMotionWindowOpened;
        Activated += RefreshMotion;
    }

    private void OnMotionWindowOpened(object? sender, EventArgs args)
    {
        ClearValue(MaxWidthProperty);
        ClearValue(MaxHeightProperty);
        _navigationMotion = UiMotionSettings.Current;
        if (_navigationMotion is not null) _navigationMotion.Changed += OnNavigationMotionChanged;
        RefreshMotion(sender, args);
        if (_windowEntrancePrepared)
        {
            _windowEntranceStart = Dispatcher.UIThread.InvokeAsync(StartWindowEntrance, DispatcherPriority.Loaded);
        }
    }

    internal void ShowWithEntrance()
    {
        if (!IsVisible && UiMotionSettings.Current?.Refresh() == true)
        {
            // Seed the first frame before the HWND is shown. Only the client surface moves.
            _windowEntrancePrepared = true;
            _windowEntranceOffset.Y = 10;
            _navigation.RenderTransform = _windowEntranceOffset;
            _navigation.Opacity = 0.52;
        }
        Show();
    }

    private void StartWindowEntrance()
    {
        _windowEntranceStart = null;
        if (!IsVisible || UiMotionSettings.Current?.Refresh() != true)
        {
            FinishWindowEntrance();
            return;
        }

        _navigation.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = WindowEntranceDuration, Easing = new SineEaseOut() }
        ];
        _windowEntranceOffset.Transitions =
        [
            new DoubleTransition { Property = TranslateTransform.YProperty, Duration = WindowEntranceDuration, Easing = new SineEaseOut() }
        ];
        _navigation.Opacity = 1;
        _windowEntranceOffset.Y = 0;
    }

    private void FinishWindowEntrance()
    {
        if (!_windowEntrancePrepared) return;

        _windowEntrancePrepared = false;
        _windowEntranceStart?.Abort();
        _windowEntranceStart = null;
        _navigation.Transitions = null;
        _windowEntranceOffset.Transitions = null;
        _navigation.Opacity = 1;
        _windowEntranceOffset.Y = 0;
    }

    private void OnNavigationTemplateApplied(object? sender, TemplateAppliedEventArgs args)
    {
        DetachSplitView();
        // The theme draws these two elements beside the SplitView, independently of its animated width.
        _paneHeader = args.NameScope.Find<Grid>("PaneToggleButtonGrid");
        if (_paneHeader is not null)
        {
            _paneTitle = args.NameScope.Find<TextBlock>("PaneTitleTextBlock");
            if (_paneTitle is not null) _paneTitle.Opacity = 0;
            _paneToggle = args.NameScope.Find<Button>("TogglePaneButton");
            if (_paneToggle is not null)
            {
                _paneToggle.TemplateApplied += OnPaneToggleTemplateApplied;
                _paneToggle.ApplyTemplate();
            }
        }
        if (args.NameScope.Find<Border>("ShadowCaster") is { } shadow) shadow.IsVisible = false;

        _navigationSplitView = args.NameScope.Find<SplitView>("RootSplitView");
        if (_navigationSplitView is not { } splitView) return;
        splitView.TemplateApplied += OnSplitViewTemplateApplied;
        splitView.PaneOpening += OnPaneChanging;
        splitView.PaneClosing += OnPaneChanging;
        splitView.ApplyTemplate();
    }

    private void OnPaneToggleTemplateApplied(object? sender, TemplateAppliedEventArgs args)
    {
        if (args.NameScope.Find<Viewbox>("IconHost") is not { } host) return;

        host.Child = new Path
        {
            Data = PaneToggleGlyph, Width = 16, Height = 16, Stroke = _primaryBrush,
            StrokeThickness = 1.25, StrokeLineCap = PenLineCap.Round, IsHitTestVisible = false
        };
    }

    private void OnSplitViewTemplateApplied(object? sender, TemplateAppliedEventArgs args)
    {
        RemoveSelectionBackground();
        _navigationPane = args.NameScope.Find<Panel>("PART_PaneRoot");
        _paneHasLayout = false;
        if (_navigationPane is { } pane)
        {
            pane.ClipToBounds = true;
            pane.Children.Insert(0, _selectionBackground);
        }
        _navigation.Resources[PaneLabelOpacity] = _navigationPane is null ? 1d : 0d;
        IBrush itemBackground = _navigationPane is null ? _selectionBrush : Brushes.Transparent;
        _navigation.Resources["NavigationViewItemBackgroundSelected"] = itemBackground;
        _navigation.Resources["NavigationViewItemBackgroundSelectedPointerOver"] = itemBackground;
        _navigation.Resources["NavigationViewItemBackgroundSelectedPressed"] = itemBackground;
        UpdateNavigationTransitions();
    }

    private void OnNavigationLayoutUpdated(object? sender, EventArgs args)
    {
        if (_navigationPane is not { } pane) return;

        if (!_paneHasLayout && pane.Bounds.Width >= _navigation.CompactPaneLength - 1)
        {
            // Initialization can assign a zero pane width before the compact style is applied.
            _paneHasLayout = true;
            UpdateNavigationTransitions();
        }
        UpdatePaneHeader(pane);
        var labelOpacity = _navigation.IsPaneOpen && pane.Bounds.Width >= _navigation.OpenPaneLength - 1 ? 1d : 0d;
        if (!Equals(_navigation.Resources[PaneLabelOpacity], labelOpacity)) _navigation.Resources[PaneLabelOpacity] = labelOpacity;
        UpdateSelectionBackground();
    }

    private void UpdatePaneHeader(Panel pane)
    {
        if (_paneHeader is not { } header || pane.TranslatePoint(new Point(pane.Bounds.Width, 0), header) is not { } edge) return;

        var clip = new Rect(0, 0, Math.Clamp(edge.X, 0, header.Bounds.Width), header.Bounds.Height);
        if (clip.Width == header.Bounds.Width)
        {
            if (header.Clip is not null) header.ClearValue(ClipProperty);
        }
        else if (header.Clip is not RectangleGeometry current || current.Rect != clip)
        {
            // Replacing Clip invalidates the compositor even when the header's own bounds stay fixed.
            header.Clip = new RectangleGeometry(clip);
        }

        if (_paneTitle is not { } title) return;
        var titleWidth = Math.Max(title.Bounds.Width, title.DesiredSize.Width);
        var fits = _navigation.IsPaneOpen && title.Bounds.Width > 0 &&
            title.TranslatePoint(new Point(titleWidth, 0), header) is { } end && end.X + 1 <= clip.Right;
        title.Opacity = fits ? 1 : 0;
    }

    private void UpdateSelectionBackground()
    {
        var layout = (_navigation.SelectedItem as NavigationViewItem)?.GetVisualDescendants()
            .OfType<Border>().FirstOrDefault(border => border.Name == "LayoutRoot" && border.TemplatedParent is NavigationViewItemPresenter);
        if (_navigationPane is not { } pane || layout is not { Bounds.Height: > 0, Bounds.Width: > 0 } ||
            layout.TranslatePoint(default, pane) is not { } position)
        {
            _selectionBackground.IsVisible = false;
            return;
        }

        _selectionBackground.Width = Math.Clamp(pane.Bounds.Width - position.X - 4, 0, layout.Bounds.Width);
        _selectionBackground.Height = layout.Bounds.Height;
        _selectionOffset.X = position.X;
        if (!_selectionBackground.IsVisible) _selectionOffset.Transitions = null;
        _selectionOffset.Y = position.Y;
        if (_selectionBackground.IsVisible) return;
        _selectionBackground.IsVisible = true;
        UpdateNavigationTransitions();
    }

    private void OnPaneChanging(object? sender, CancelRoutedEventArgs args) => RefreshMotion(sender, args);

    private void RefreshMotion(object? sender, EventArgs args)
    {
        UiMotionSettings.Current?.Refresh();
        UpdateNavigationTransitions();
    }

    private void OnNavigationMotionChanged(object? sender, EventArgs args)
    {
        if (_navigationMotion?.Enabled == false) FinishWindowEntrance();
        UpdateNavigationTransitions();
    }

    private void UpdateNavigationTransitions()
    {
        var enabled = UiMotionSettings.Current?.Enabled == true;
        if (_navigationPane is { } pane) pane.Transitions = enabled && _paneHasLayout ? _paneTransitions : null;
        _selectionOffset.Transitions = enabled && _selectionBackground.IsVisible ? _selectionTransitions : null;
    }

    private void DetachSplitView()
    {
        if (_navigationSplitView is { } splitView)
        {
            splitView.TemplateApplied -= OnSplitViewTemplateApplied;
            splitView.PaneOpening -= OnPaneChanging;
            splitView.PaneClosing -= OnPaneChanging;
        }

        RemoveSelectionBackground();
        _navigationSplitView = null;
        _navigationPane = null;
        if (_paneHeader is not null) _paneHeader.ClearValue(ClipProperty);
        _paneHeader = null;
        if (_paneTitle is not null) _paneTitle.ClearValue(OpacityProperty);
        _paneTitle = null;
        if (_paneToggle is not null) _paneToggle.TemplateApplied -= OnPaneToggleTemplateApplied;
        _paneToggle = null;
    }

    private void RemoveSelectionBackground()
    {
        _selectionOffset.Transitions = null;
        _selectionBackground.IsVisible = false;
        _navigationPane?.Children.Remove(_selectionBackground);
    }

    private void StopNavigationMotion()
    {
        FinishWindowEntrance();
        _pageEntrance.Stop();
        if (_navigationMotion is not null) _navigationMotion.Changed -= OnNavigationMotionChanged;
        _navigation.TemplateApplied -= OnNavigationTemplateApplied;
        _navigation.LayoutUpdated -= OnNavigationLayoutUpdated;
        Opened -= OnMotionWindowOpened;
        Activated -= RefreshMotion;
        DetachSplitView();
    }
}
