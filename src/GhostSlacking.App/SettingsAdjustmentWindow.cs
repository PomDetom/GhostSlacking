using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Windowing;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal abstract class SettingsAdjustmentWindow : AppWindow
{
    protected readonly AppSettings Initial;
    protected readonly StackPanel Editors = new() { Spacing = 12 };
    protected readonly StackPanel Preview = new() { Spacing = 10 };
    protected readonly UiLanguage Language;
    internal AppSettings? AcceptedSettings { get; private set; }
    internal Button CompleteButton { get; }

    protected SettingsAdjustmentWindow(AppSettings initial, string titleKey)
    {
        Initial = initial.Normalize();
        Language = Initial.Language;
        Title = T(titleKey);
        Icon = AppIcon.TitleBarImage;
        Width = 960;
        Height = 700;
        MinWidth = 740;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24) };
        layout.Children.Add(new StackPanel
        {
            Margin = new Thickness(0, 4, 0, 20), Spacing = 6,
            Children =
            {
                new TextBlock { Text = Title, FontSize = 24, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = T("adjustDraftHint"), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 }
            }
        });
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("5*,6*") };
        body.Children.Add(new ScrollViewer { Content = Editors, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var previewScroll = new ScrollViewer { Content = Preview, Margin = new Thickness(24, 0, 0, 0), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetColumn(previewScroll, 1);
        body.Children.Add(previewScroll);
        Grid.SetRow(body, 1);
        layout.Children.Add(body);
        var cancel = new Button { Content = T("cancel"), MinWidth = 96 };
        cancel.Click += (_, _) => Close();
        CompleteButton = new Button { Content = T("adjustComplete"), MinWidth = 96 };
        CompleteButton.Classes.Add("accent");
        CompleteButton.Click += (_, _) => Complete();
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10, Margin = new Thickness(0, 20, 0, 0), Children = { cancel, CompleteButton }
        };
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        Content = layout;
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.Escape) { args.Handled = true; Close(); }
        }, RoutingStrategies.Tunnel);
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        Opened += (_, _) =>
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null) return;
            var available = screen.WorkingArea.Size.ToSize(screen.Scaling);
            MinWidth = Math.Min(MinWidth, available.Width - 24);
            MinHeight = Math.Min(MinHeight, available.Height - 24);
            Width = Math.Min(Width, available.Width - 24);
            Height = Math.Min(Height, available.Height - 24);
        };
        ApplyPalette();
    }

    private void ApplyPalette()
    {
        var palette = AppTheme.Palette(ActualThemeVariant);
        Background = new SolidColorBrush(palette.Surface);
        Foreground = new SolidColorBrush(palette.Primary);
        TitleBar.BackgroundColor = palette.Surface;
        TitleBar.ForegroundColor = palette.Primary;
    }

    protected string T(string key) => UiText.Text(Language, key);
    protected TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 };
    protected SliderNumberEditor AddNumber(string label, string unit, double value, double min, double max, double step, double fallback)
    {
        var editor = new SliderNumberEditor(T(label), unit, value, min, max, step, fallback, T("resetSetting"));
        Editors.Children.Add(editor);
        return editor;
    }

    internal void Complete()
    {
        // Commit the focused NumberBox text before taking the local draft.
        CompleteButton.Focus();
        AcceptedSettings = ReadDraft().Normalize();
        Close(AcceptedSettings);
    }

    protected abstract AppSettings ReadDraft();
}
