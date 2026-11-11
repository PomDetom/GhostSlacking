using GhostSlacking.Core;

namespace GhostSlacking.App;

internal enum AdjustmentKind
{
    Diameter, DiameterStep, SoftEdgeWidth, BlurLevel, Shape, FrameRate,
    NotificationRadius, NotificationWidth, NotificationHeight, NotificationHorizontal, NotificationVertical
}

internal sealed record SettingAdjustment(string Label, string Unit, double Minimum, double Maximum, double Step)
{
    internal static SettingAdjustment For(AdjustmentKind kind) => kind switch
    {
        AdjustmentKind.Diameter => new("diameter", "px", 64, 800, 8),
        AdjustmentKind.DiameterStep => new("diameterStep", "px", 8, 256, 8),
        AdjustmentKind.SoftEdgeWidth => new("softEdgeWidth", "px", 0, 128, 4),
        AdjustmentKind.BlurLevel => new("blurLevel", "", 0, 2, 1),
        AdjustmentKind.Shape => new("shape", "", 0, 2, 1),
        AdjustmentKind.FrameRate => new("peekFrameRateLimit", "", 0, 3, 1),
        AdjustmentKind.NotificationRadius => new("notificationRadius", "DIP", 0, 48, 1),
        AdjustmentKind.NotificationWidth => new("notificationWidth", "DIP", 100, 300, 1),
        AdjustmentKind.NotificationHeight => new("notificationHeight", "DIP", 32, 96, 1),
        AdjustmentKind.NotificationHorizontal => new("notificationHorizontalPosition", "%", 0, 100, 1),
        AdjustmentKind.NotificationVertical => new("notificationVerticalPosition", "%", 0, 100, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static bool IsNotification(AdjustmentKind kind) => kind >= AdjustmentKind.NotificationRadius;

    internal static double Read(AppSettings settings, AdjustmentKind kind) => kind switch
    {
        AdjustmentKind.Diameter => settings.RevealDiameterPx,
        AdjustmentKind.DiameterStep => settings.RevealDiameterStepPx,
        AdjustmentKind.SoftEdgeWidth => settings.RevealSoftEdgeWidthPx,
        AdjustmentKind.BlurLevel => (int)settings.RevealBlurLevel,
        AdjustmentKind.Shape => (int)settings.RevealShape,
        AdjustmentKind.FrameRate => FrameIndex(settings.PeekFrameRateLimit),
        AdjustmentKind.NotificationRadius => settings.NotificationHud.CornerRadius,
        AdjustmentKind.NotificationWidth => settings.NotificationHud.Width,
        AdjustmentKind.NotificationHeight => settings.NotificationHud.Height,
        AdjustmentKind.NotificationHorizontal => settings.NotificationHud.HorizontalPosition,
        AdjustmentKind.NotificationVertical => settings.NotificationHud.VerticalPosition,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static AppSettings Apply(AppSettings settings, AdjustmentKind kind, double value)
    {
        if (!double.IsFinite(value)) return settings;
        var spec = For(kind);
        value = Math.Clamp(value, spec.Minimum, spec.Maximum);
        var integer = (int)Math.Round(value);
        return kind switch
        {
            AdjustmentKind.Diameter => settings with { RevealDiameterPx = integer },
            AdjustmentKind.DiameterStep => settings with { RevealDiameterStepPx = integer },
            AdjustmentKind.SoftEdgeWidth => settings with { RevealSoftEdgeWidthPx = integer },
            AdjustmentKind.BlurLevel => settings with { RevealBlurLevel = (RevealBlurLevel)integer },
            AdjustmentKind.Shape => settings with { RevealShape = (RevealShape)integer },
            AdjustmentKind.FrameRate => settings with { PeekFrameRateLimit = FrameValue(integer) },
            AdjustmentKind.NotificationRadius => settings with { NotificationHud = settings.NotificationHud with { CornerRadius = value } },
            AdjustmentKind.NotificationWidth => settings with { NotificationHud = settings.NotificationHud with { Width = value } },
            AdjustmentKind.NotificationHeight => settings with { NotificationHud = settings.NotificationHud with { Height = value } },
            AdjustmentKind.NotificationHorizontal => settings with { NotificationHud = settings.NotificationHud with { HorizontalPosition = value } },
            AdjustmentKind.NotificationVertical => settings with { NotificationHud = settings.NotificationHud with { VerticalPosition = value } },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    internal static int FrameIndex(PeekFrameRateLimit value) => value switch
    { PeekFrameRateLimit.Fps60 => 1, PeekFrameRateLimit.Fps90 => 2, PeekFrameRateLimit.Fps120 => 3, _ => 0 };

    internal static PeekFrameRateLimit FrameValue(int index) => index switch
    { 1 => PeekFrameRateLimit.Fps60, 2 => PeekFrameRateLimit.Fps90, 3 => PeekFrameRateLimit.Fps120, _ => PeekFrameRateLimit.Auto };

    internal static string Format(AppSettings settings, AdjustmentKind kind, UiLanguage language)
    {
        var value = Read(settings, kind);
        return kind switch
        {
            AdjustmentKind.Shape => UiText.Text(language, (int)value switch { 0 => "circle", 1 => "rectangle", _ => "roundedRectangle" }),
            AdjustmentKind.BlurLevel => UiText.Text(language, (int)value switch { 0 => "blurLow", 1 => "blurMedium", _ => "blurHigh" }),
            AdjustmentKind.FrameRate => value == 0 ? UiText.Text(language, "peekFrameRateAuto") : $"{(value + 1) * 30} FPS",
            _ => $"{value:0.##} {For(kind).Unit}"
        };
    }
}
