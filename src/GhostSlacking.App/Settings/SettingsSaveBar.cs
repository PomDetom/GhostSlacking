using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls;
using Path = Avalonia.Controls.Shapes.Path;

namespace GhostSlacking.App;

internal sealed class SettingsSaveBar : Border, IDisposable
{
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(200);
    private static readonly Geometry ModifiedIcon = Geometry.Parse(
        "M 12,3 L 22,21 L 2,21 Z M 12,9 L 12,14 M 12,17 L 12,17.1");
    private static readonly Geometry SavedIcon = Geometry.Parse(
        "M 22,12 A 10,10 0 1 1 12,2 A 10,10 0 0 1 22,12 M 7,12 L 10,15 L 17,8");
    private static readonly Geometry ErrorIcon = Geometry.Parse(
        "M 22,12 A 10,10 0 1 1 12,2 A 10,10 0 0 1 22,12 M 12,7 L 12,12 M 12,16 L 12,16.1");
    private readonly TextBlock _title;
    private readonly Path _icon;
    private readonly SolidColorBrush _iconBrush = new();
    private readonly StackPanel _actions;
    private readonly Button _cancel;
    private readonly Button _save;
    private readonly TranslateTransform _translation = new(0, 14);
    private IDisposable? _hideRegistration;
    private InfoBarSeverity _severity;
    private bool _open;
    private bool _disposed;

    public SettingsSaveBar(IBrush surface, IBrush border, IBrush primary)
    {
        Background = surface;
        BorderBrush = border;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(14, 8);
        Height = 60;
        ClipToBounds = true;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(16);
        BoxShadow = BoxShadows.Parse("0 6 24 0 #2017332D");
        RenderTransform = _translation;
        Opacity = 0;
        IsVisible = false;
        IsEnabled = false;

        _icon = new Path
        {
            Data = ModifiedIcon,
            Stroke = _iconBrush,
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform,
            Width = 18,
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0)
        };
        _title = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeight.Medium,
            Foreground = primary,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetLiveSetting(_title, AutomationLiveSetting.Polite);
        var status = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto") };
        status.Children.Add(_icon);
        Grid.SetColumn(_title, 1);
        status.Children.Add(_title);

        _cancel = new Button { MinWidth = 62, MinHeight = 34, Padding = new Thickness(12, 6) };
        _save = new Button { MinWidth = 62, MinHeight = 34, Padding = new Thickness(12, 6) };
        _save.Styles.Add(new Style(selector => selector.OfType<Button>())
        {
            Setters =
            {
                new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.Parse("#087F79"))),
                new Setter(Button.ForegroundProperty, Brushes.White),
                new Setter(Button.CornerRadiusProperty, new CornerRadius(7))
            }
        });
        _save.Styles.Add(new Style(selector => selector.OfType<Button>().Class(":pointerover"))
        {
            Setters = { new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.Parse("#066C67"))) }
        });
        _save.Styles.Add(new Style(selector => selector.OfType<Button>().Class(":pressed"))
        {
            Setters = { new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.Parse("#055953"))) }
        });
        _cancel.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);
        _save.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
        _actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _cancel, _save }
        };
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto") };
        content.Children.Add(status);
        Grid.SetColumn(_actions, 1);
        content.Children.Add(_actions);
        Child = content;
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        ApplyPalette();
    }

    public event EventHandler? CancelRequested;
    public event EventHandler? SaveRequested;

    public void SetLanguage(Core.UiLanguage language)
    {
        _cancel.Content = UiText.Text(language, "cancel");
        _save.Content = UiText.Text(language, "save");
    }

    public void UpdateTitle(string title)
    {
        _title.Text = title;
        ToolTip.SetTip(this, title);
    }

    public void ShowMessage(string title, string description, InfoBarSeverity severity, bool showActions)
    {
        if (_disposed)
        {
            return;
        }

        _hideRegistration?.Dispose();
        _hideRegistration = null;
        _open = true;
        _title.Text = title;
        _severity = severity;
        _icon.Data = severity switch
        {
            InfoBarSeverity.Success => SavedIcon,
            InfoBarSeverity.Error => ErrorIcon,
            _ => ModifiedIcon
        };
        _actions.IsVisible = showActions;
        ToolTip.SetTip(this, string.IsNullOrEmpty(description) ? title : $"{title} · {description}");
        ApplyPalette();
        var appearing = !IsVisible;
        ConfigureAnimation();
        IsVisible = true;
        IsEnabled = true;
        if (appearing && FAUISettings.AreAnimationsEnabled())
        {
            Dispatcher.UIThread.Post(Reveal, DispatcherPriority.Render);
        }
        else
        {
            Reveal();
        }
    }

    public void Hide()
    {
        _open = false;
        IsEnabled = false;
        _hideRegistration?.Dispose();
        _hideRegistration = null;
        ConfigureAnimation();
        Opacity = 0;
        _translation.Y = 14;
        if (!IsVisible)
        {
            return;
        }

        if (FAUISettings.AreAnimationsEnabled())
        {
            _hideRegistration = DispatcherTimer.RunOnce(() =>
            {
                _hideRegistration = null;
                if (!_open)
                {
                    IsVisible = false;
                }
            }, AnimationDuration);
        }
        else
        {
            IsVisible = false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _open = false;
        _hideRegistration?.Dispose();
        _hideRegistration = null;
    }

    private void Reveal()
    {
        if (_open && !_disposed)
        {
            Opacity = 1;
            _translation.Y = 0;
        }
    }

    private void ConfigureAnimation()
    {
        if (FAUISettings.AreAnimationsEnabled())
        {
            Transitions ??= [new DoubleTransition { Property = OpacityProperty, Duration = AnimationDuration }];
            _translation.Transitions ??= [new DoubleTransition { Property = TranslateTransform.YProperty, Duration = AnimationDuration }];
        }
        else
        {
            Transitions = null;
            _translation.Transitions = null;
        }
    }

    private void ApplyPalette()
    {
        var dark = AppTheme.IsDark(ActualThemeVariant);
        _iconBrush.Color = Color.Parse(_severity == InfoBarSeverity.Error
            ? dark ? "#FFAAA3" : "#AC3030"
            : dark ? "#71DB9C" : "#198754");
        BoxShadow = BoxShadows.Parse(dark ? "0 6 24 0 #66000000" : "0 6 24 0 #2017332D");
    }
}
