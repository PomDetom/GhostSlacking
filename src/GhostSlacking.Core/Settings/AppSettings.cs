
namespace GhostSlacking.Core;

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 10;
    public UiLanguage Language { get; init; } = UiLanguage.Chinese;
    public UiThemeMode ThemeMode { get; init; } = UiThemeMode.System;
    public NotificationStyle NotificationStyle { get; init; } = NotificationStyle.Card;
    public NotificationPlacement CardNotificationPlacement { get; init; } = new(1, 1);
    public NotificationPlacement CapsuleNotificationPlacement { get; init; } = new(1, 1);
    public NotificationPlacement ChatNotificationPlacement { get; init; } = new(0, 1);
    public NotificationHudSettings NotificationHud { get; init; } = new();
    public int RevealDiameterPx { get; init; } = 144;
    public int RevealDiameterStepPx { get; init; } = 16;
    public int RevealSoftEdgeWidthPx { get; init; } = 16;
    public RevealBlurLevel RevealBlurLevel { get; init; } = RevealBlurLevel.Low;
    public RevealShape RevealShape { get; init; } = RevealShape.RoundedRectangle;
    public int PeekVirtualKey { get; init; } = 0x12;
    public PeekTrigger PeekTrigger { get; init; } = PeekTrigger.Toggle;
    public PeekFrameRateLimit PeekFrameRateLimit { get; init; } = PeekFrameRateLimit.Auto;
    public UpdateChannel UpdateChannel { get; init; } = UpdateChannel.Stable;
    public HotkeyBinding PickHotkey { get; init; } = new(0x50, ShortcutModifiers.Control | ShortcutModifiers.Alt);
    public int WindowToggleVirtualKey { get; init; } = 0x47;
    public HotkeyBinding WindowToggleHotkey { get; init; } = new(0x47, ShortcutModifiers.Control | ShortcutModifiers.Alt);
    public HotkeyBinding RestoreHotkey { get; init; } = new(0x52, ShortcutModifiers.Control | ShortcutModifiers.Alt);
    public HotkeyBinding RestoreAllHotkey { get; init; } = new(0x52, ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift);
    public HotkeyBinding SettingsHotkey { get; init; } = new(0x53, ShortcutModifiers.Control | ShortcutModifiers.Alt);
    public HotkeyBinding ExitHotkey { get; init; } = new(0x51, ShortcutModifiers.Control | ShortcutModifiers.Alt);
    public HotkeyBinding RevealDiameterIncreaseHotkey { get; init; } = new(0, ShortcutModifiers.None);
    public HotkeyBinding RevealDiameterDecreaseHotkey { get; init; } = new(0, ShortcutModifiers.None);
    public bool ExperimentalBackgroundInteraction { get; init; }
    public bool ContinuousBackgroundInteraction { get; init; }
    public HotkeyBinding BackgroundInteractionHotkey { get; init; } = HotkeyBinding.Disabled;
    public bool StartWithWindows { get; init; }
    public bool RestoreOnExit { get; init; } = true;
    public LogLevel MinimumLogLevel { get; init; } = LogLevel.Info;

    public AppSettings Normalize() => this with
    {
        SchemaVersion = 10,
        Language = Language is UiLanguage.Chinese or UiLanguage.English ? Language : UiLanguage.Chinese,
        ThemeMode = ThemeMode is UiThemeMode.System or UiThemeMode.Light or UiThemeMode.Dark
            ? ThemeMode
            : UiThemeMode.System,
        NotificationStyle = NotificationStyle is NotificationStyle.Card or NotificationStyle.Capsule or NotificationStyle.Chat
            ? NotificationStyle
            : NotificationStyle.Card,
        CardNotificationPlacement = CardNotificationPlacement.Normalize(),
        CapsuleNotificationPlacement = CapsuleNotificationPlacement.Normalize(),
        ChatNotificationPlacement = ChatNotificationPlacement.Normalize(),
        NotificationHud = NotificationHud?.Normalize() ?? new NotificationHudSettings(),
        RevealDiameterPx = Math.Clamp(RevealDiameterPx, 64, 800),
        RevealDiameterStepPx = Math.Clamp(RevealDiameterStepPx, 8, 256),
        RevealSoftEdgeWidthPx = Math.Clamp(RevealSoftEdgeWidthPx, 0, 128),
        RevealBlurLevel = RevealBlurLevel is RevealBlurLevel.Low or RevealBlurLevel.Medium or RevealBlurLevel.High
            ? RevealBlurLevel
            : RevealBlurLevel.Low,
        RevealShape = RevealShape is RevealShape.Circle or RevealShape.Rectangle or RevealShape.RoundedRectangle
            ? RevealShape
            : RevealShape.RoundedRectangle,
        PeekVirtualKey = PeekVirtualKey is >= 1 and <= 255 ? PeekVirtualKey : 0x12,
        PeekTrigger = PeekTrigger is PeekTrigger.Hold or PeekTrigger.Toggle ? PeekTrigger : PeekTrigger.Toggle,
        PeekFrameRateLimit = PeekFrameRateLimit is PeekFrameRateLimit.Auto or
            PeekFrameRateLimit.Fps60 or
            PeekFrameRateLimit.Fps90 or
            PeekFrameRateLimit.Fps120
                ? PeekFrameRateLimit
                : PeekFrameRateLimit.Auto,
        UpdateChannel = UpdateChannel is UpdateChannel.Stable or UpdateChannel.Test
            ? UpdateChannel
            : UpdateChannel.Stable,
        PickHotkey = PickHotkey.Normalize(0x50, ShortcutModifiers.Control | ShortcutModifiers.Alt),
        BackgroundInteractionHotkey = BackgroundInteractionHotkey.Normalize(0, ShortcutModifiers.None),
        WindowToggleVirtualKey = WindowToggleVirtualKey is >= 1 and <= 255 ? WindowToggleVirtualKey : 0x47,
        WindowToggleHotkey = NormalizeWindowToggleHotkey(),
        RestoreHotkey = RestoreHotkey.Normalize(0x52, ShortcutModifiers.Control | ShortcutModifiers.Alt),
        RestoreAllHotkey = RestoreAllHotkey.Normalize(0x52, ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift),
        SettingsHotkey = SettingsHotkey.Normalize(0x53, ShortcutModifiers.Control | ShortcutModifiers.Alt),
        ExitHotkey = ExitHotkey.Normalize(0x51, ShortcutModifiers.Control | ShortcutModifiers.Alt),
        RevealDiameterIncreaseHotkey = RevealDiameterIncreaseHotkey.Normalize(0, ShortcutModifiers.None),
        RevealDiameterDecreaseHotkey = RevealDiameterDecreaseHotkey.Normalize(0, ShortcutModifiers.None),
        MinimumLogLevel = MinimumLogLevel is LogLevel.Error or LogLevel.Warning or LogLevel.Info or LogLevel.Debug
            ? MinimumLogLevel
            : LogLevel.Info
    };

    private HotkeyBinding NormalizeWindowToggleHotkey()
    {
        var defaultBinding = new HotkeyBinding(0x47, ShortcutModifiers.Control | ShortcutModifiers.Alt);
        var binding = WindowToggleHotkey.Normalize(defaultBinding.VirtualKey, defaultBinding.Modifiers);
        return binding == defaultBinding && WindowToggleVirtualKey is >= 1 and <= 255
            ? binding with { VirtualKey = WindowToggleVirtualKey }
            : binding;
    }

    public AppSettings AdjustRevealDiameter(int direction)
    {
        var normalized = Normalize();
        if (direction == 0)
        {
            return normalized;
        }

        return normalized with
        {
            RevealDiameterPx = Math.Clamp(
                normalized.RevealDiameterPx + (Math.Sign(direction) * normalized.RevealDiameterStepPx),
                64,
                800)
        };
    }
}
