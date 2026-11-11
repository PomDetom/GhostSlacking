using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GhostSlacking.Core;

namespace GhostSlacking.App;

internal sealed class NotificationAdjustmentWindow : SettingsAdjustmentWindow
{
    internal readonly SliderNumberEditor Radius;
    internal readonly SliderNumberEditor MaximumWidth;
    internal readonly SliderNumberEditor HudHeight;
    internal readonly SliderNumberEditor HorizontalPosition;
    internal readonly SliderNumberEditor VerticalPosition;
    internal readonly NotificationContent Sample = new();
    private readonly NotificationContent _miniature = new();
    private readonly Canvas _screen = new() { Height = 180, ClipToBounds = true };
    internal readonly ComboBox MessageChoice;

    internal NotificationAdjustmentWindow(AppSettings initial) : base(initial, "adjustNotificationTitle")
    {
        var hud = Initial.NotificationHud;
        Radius = AddNumber("notificationRadius", "DIP", hud.CornerRadius, 0, 48, 1, 10);
        MaximumWidth = AddNumber("notificationWidth", "DIP", hud.Width, 100, 300, 1, 132);
        HudHeight = AddNumber("notificationHeight", "DIP", hud.Height, 32, 96, 1, 44);
        HorizontalPosition = AddNumber("notificationHorizontalPosition", "%", hud.HorizontalPosition, 0, 100, 1, 50);
        VerticalPosition = AddNumber("notificationVerticalPosition", "%", hud.VerticalPosition, 0, 100, 1, 100);
        Preview.Children.Add(new TextBlock { Text = T("adjustActualSize"), FontWeight = FontWeight.SemiBold });
        MessageChoice = new ComboBox { ItemsSource = new[] { T("adjustShortText"), T("adjustLongText") }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        Avalonia.Automation.AutomationProperties.SetName(MessageChoice, T("adjustSample"));
        Preview.Children.Add(MessageChoice);
        Sample.HorizontalAlignment = HorizontalAlignment.Center;
        Preview.Children.Add(new Border
        {
            MinHeight = 120, Padding = new Thickness(8), CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(20, 28, 178, 165)), Child = Sample
        });
        Preview.Children.Add(Note(T("adjustWidthHint")));
        Preview.Children.Add(new TextBlock { Text = T("adjustScreenPosition"), FontWeight = FontWeight.SemiBold });
        _screen.Background = new SolidColorBrush(Color.FromArgb(24, 28, 178, 165));
        _screen.Children.Add(_miniature);
        Preview.Children.Add(_screen);
        Preview.Children.Add(Note(T("adjustScreenHint")));
        foreach (var editor in new[] { Radius, MaximumWidth, HudHeight, HorizontalPosition, VerticalPosition })
            editor.ValueChanged += (_, _) => RefreshPreview();
        MessageChoice.SelectionChanged += (_, _) => RefreshPreview();
        _screen.SizeChanged += (_, _) => UpdatePosition();
        Sample.ContentMetricsChanged += (_, _) => RefreshPreview();
        ScalingChanged += (_, _) => RefreshPreview();
        PositionChanged += (_, _) => UpdatePosition();
        Opened += (_, _) => RefreshPreview();
        RefreshPreview();
    }

    protected override AppSettings ReadDraft() => Initial with
    {
        NotificationHud = new NotificationHudSettings
        {
            CornerRadius = Radius.Value, Width = MaximumWidth.Value, Height = HudHeight.Value,
            HorizontalPosition = HorizontalPosition.Value, VerticalPosition = VerticalPosition.Value
        }.Normalize()
    };

    private void RefreshPreview()
    {
        var hud = ReadDraft().NotificationHud;
        var message = T(MessageChoice.SelectedIndex == 1 ? "adjustLongMessage" : "notificationPreviewMessage");
        Sample.Update(message, UserNotificationSeverity.Info, false);
        Sample.MeasurePreferredWidth(hud, RenderScaling);
        Sample.Configure(hud);
        _miniature.Update(message, UserNotificationSeverity.Info, false);
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        var hud = ReadDraft().NotificationHud;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var work = screen?.WorkingArea ?? new PixelRect(0, 0, 1440, 900);
        var scaling = screen?.Scaling ?? 1;
        var preferred = _miniature.MeasurePreferredWidth(hud, scaling);
        var geometry = NotificationLayout.Calculate(work, scaling, hud, preferred);
        _miniature.Configure(hud, geometry);
        var scale = Math.Min(Math.Max(1, _screen.Bounds.Width) / work.Width, _screen.Height / work.Height);
        _miniature.RenderTransformOrigin = RelativePoint.TopLeft;
        _miniature.RenderTransform = new ScaleTransform(scale * scaling, scale * scaling);
        var originX = (_screen.Bounds.Width - work.Width * scale) / 2;
        var originY = (_screen.Height - work.Height * scale) / 2;
        Canvas.SetLeft(_miniature, originX + (geometry.Position.X - work.X) * scale);
        Canvas.SetTop(_miniature, originY + (geometry.Position.Y - work.Y) * scale);
    }
}
