using System.Drawing;

namespace GhostSlacking.Core;

public enum GhostState
{
    Idle,
    Picking,
    Preparing,
    Ghost,
    Reveal,
    Visible,
    Restoring,
    RecoveryError
}

public enum RevealShape
{
    Circle,
    Rectangle,
    RoundedRectangle
}

public enum RevealBlurLevel
{
    Low,
    Medium,
    High
}

public enum PeekTrigger
{
    Hold,
    Toggle
}

[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4
}

public readonly record struct HotkeyBinding(int VirtualKey, ShortcutModifiers Modifiers)
{
    public static HotkeyBinding Disabled => new(0, ShortcutModifiers.None);

    public bool IsDisabled => this == Disabled;

    public bool IsValid() => IsDisabled ||
        (VirtualKey is >= 1 and <= 255 &&
         !IsModifierVirtualKey(VirtualKey) &&
         (Modifiers & ~(ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift)) == 0);

    public static bool IsModifierVirtualKey(int virtualKey) =>
        virtualKey is 0x10 or 0x11 or 0x12 or >= 0xA0 and <= 0xA5;

    public HotkeyBinding Normalize(int defaultVirtualKey, ShortcutModifiers defaultModifiers) =>
        IsValid() ? this : new HotkeyBinding(defaultVirtualKey, defaultModifiers);
}

public enum LogLevel
{
    Error,
    Warning,
    Info,
    Debug
}

public enum UiLanguage
{
    Chinese,
    English
}

public enum UiThemeMode
{
    System,
    Light,
    Dark
}

// Kept as CircleRegion for compatibility with the V0.1 core API. The region
// now carries the selected shape and is used for all Reveal geometries.
public readonly record struct CircleRegion(
    int CenterX,
    int CenterY,
    int DiameterPx,
    RevealShape Shape = RevealShape.Circle,
    int CornerRadiusPx = 0)
{
    public int Radius => DiameterPx / 2;
    public int CornerRadius => Math.Clamp(CornerRadiusPx, 0, Radius);

    public override string ToString() => $"shape={Shape}, center=({CenterX},{CenterY}), diameter={DiameterPx}";
}

public sealed record RevealSettings
{
    public int DiameterPx { get; init; } = 144;
    public int SoftEdgeWidthPx { get; init; } = 16;
    public RevealBlurLevel BlurLevel { get; init; } = RevealBlurLevel.Low;
    public RevealShape Shape { get; init; } = RevealShape.RoundedRectangle;
    public PeekTrigger Trigger { get; init; } = PeekTrigger.Toggle;

    public bool IsValid() => DiameterPx is >= 64 and <= 800 &&
        SoftEdgeWidthPx is >= 0 and <= 128 &&
        BlurLevel is RevealBlurLevel.Low or RevealBlurLevel.Medium or RevealBlurLevel.High &&
        Shape is RevealShape.Circle or RevealShape.Rectangle or RevealShape.RoundedRectangle &&
        Trigger is PeekTrigger.Hold or PeekTrigger.Toggle;
}

public sealed record RevealVisualState(
    nint TargetHwnd,
    Rectangle WindowBounds,
    CircleRegion CoreRegion,
    CircleRegion ContentRegion,
    int FeatherWidthPx,
    float BlurAmountPx);

public sealed record WindowStyleSnapshot(nint Style, nint ExtendedStyle);

public sealed record WindowPlacementSnapshot(
    int Flags,
    int ShowCommand,
    Point MinPosition,
    Point MaxPosition,
    Rectangle NormalPosition,
    Rectangle DevicePosition)
{
    public bool IsMinimized => ShowCommand is 2 or 6 or 7 or 11;
    public bool IsMaximized => ShowCommand == 3;
}

public sealed record WindowSnapshot
{
    public required nint Hwnd { get; init; }
    public required uint ProcessId { get; init; }
    public string? ProcessName { get; init; }
    public required string ProcessStartIdentity { get; init; }
    public required Rectangle ScreenBounds { get; init; }
    public required bool WasVisible { get; init; }
    public required bool WasMinimized { get; init; }
    public required WindowPlacementSnapshot Placement { get; init; }
    public byte[]? OriginalRegionData { get; init; }
    public int? OriginalSystemBackdropType { get; init; }
    public int? OriginalNonClientRenderingPolicy { get; init; }
    public int? OriginalWindowCornerPreference { get; init; }
    public int? OriginalBorderColor { get; init; }
    public required WindowStyleSnapshot Styles { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public int SchemaVersion { get; init; } = 2;
}

public sealed record TargetWindow
{
    public required nint Hwnd { get; init; }
    public required uint ProcessId { get; init; }
    public string? Title { get; init; }
    public string? ProcessName { get; init; }
    public required Rectangle ScreenBounds { get; init; }
}

public sealed record WindowObservation
{
    public required nint Hwnd { get; init; }
    public required uint ProcessId { get; init; }
    public required Rectangle ScreenBounds { get; init; }
    public required bool IsVisible { get; init; }
    public required bool IsMinimized { get; init; }
    public required bool IsMaximized { get; init; }
    public string? ProcessName { get; init; }
}

public sealed record GhostWindowProfile(WindowSnapshot Original, RevealSettings Reveal)
{
    public nint Hwnd => Original.Hwnd;
}

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 4;
    public UiLanguage Language { get; init; } = UiLanguage.Chinese;
    public UiThemeMode ThemeMode { get; init; } = UiThemeMode.System;
    public int RevealDiameterPx { get; init; } = 144;
    public int RevealDiameterStepPx { get; init; } = 16;
    public int RevealSoftEdgeWidthPx { get; init; } = 16;
    public RevealBlurLevel RevealBlurLevel { get; init; } = RevealBlurLevel.Low;
    public RevealShape RevealShape { get; init; } = RevealShape.RoundedRectangle;
    public int PeekVirtualKey { get; init; } = 0x12;
    public PeekTrigger PeekTrigger { get; init; } = PeekTrigger.Toggle;
    public HotkeyBinding PickHotkey { get; init; } = new(0x50, ShortcutModifiers.Control | ShortcutModifiers.Alt);
    public int WindowToggleVirtualKey { get; init; } = 0x47;
    public HotkeyBinding WindowToggleHotkey { get; init; } = new(0x47, ShortcutModifiers.Control | ShortcutModifiers.Alt);
    public HotkeyBinding RestoreHotkey { get; init; } = new(0x52, ShortcutModifiers.Control | ShortcutModifiers.Alt);
    public HotkeyBinding RestoreAllHotkey { get; init; } = new(0x52, ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift);
    public HotkeyBinding SettingsHotkey { get; init; } = new(0x53, ShortcutModifiers.Control | ShortcutModifiers.Alt);
    public HotkeyBinding ExitHotkey { get; init; } = new(0x51, ShortcutModifiers.Control | ShortcutModifiers.Alt);
    public HotkeyBinding RevealDiameterIncreaseHotkey { get; init; } = new(0, ShortcutModifiers.None);
    public HotkeyBinding RevealDiameterDecreaseHotkey { get; init; } = new(0, ShortcutModifiers.None);
    public bool StartWithWindows { get; init; }
    public bool RestoreOnExit { get; init; } = true;
    public LogLevel MinimumLogLevel { get; init; } = LogLevel.Info;

    public AppSettings Normalize() => this with
    {
        SchemaVersion = 4,
        Language = Language is UiLanguage.Chinese or UiLanguage.English ? Language : UiLanguage.Chinese,
        ThemeMode = ThemeMode is UiThemeMode.System or UiThemeMode.Light or UiThemeMode.Dark
            ? ThemeMode
            : UiThemeMode.System,
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
        PickHotkey = PickHotkey.Normalize(0x50, ShortcutModifiers.Control | ShortcutModifiers.Alt),
        WindowToggleVirtualKey = WindowToggleVirtualKey is >= 1 and <= 255 ? WindowToggleVirtualKey : 0x47,
        WindowToggleHotkey = NormalizeWindowToggleHotkey(),
        RestoreHotkey = RestoreHotkey.Normalize(0x52, ShortcutModifiers.Control | ShortcutModifiers.Alt),
        RestoreAllHotkey = RestoreAllHotkey.Normalize(0x52, ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift),
        SettingsHotkey = SettingsHotkey.Normalize(0x53, ShortcutModifiers.Control | ShortcutModifiers.Alt),
        ExitHotkey = ExitHotkey.Normalize(0x51, ShortcutModifiers.Control | ShortcutModifiers.Alt),
        RevealDiameterIncreaseHotkey = RevealDiameterIncreaseHotkey.Normalize(0, ShortcutModifiers.None),
        RevealDiameterDecreaseHotkey = RevealDiameterDecreaseHotkey.Normalize(0, ShortcutModifiers.None)
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

public sealed record NativeResult(bool Success, string Operation, int ErrorCode = 0, string? ErrorMessage = null)
{
    public static NativeResult Ok(string operation) => new(true, operation);
    public static NativeResult Failed(string operation, int errorCode, string? message = null) => new(false, operation, errorCode, message);
}

public sealed record OperationResult<T>(bool Success, T? Value, string? Error = null, int ErrorCode = 0)
{
    public static OperationResult<T> Ok(T value) => new(true, value);
    public static OperationResult<T> Failed(string error, int errorCode = 0) => new(false, default, error, errorCode);
}

public sealed record RestoreItemResult(nint Hwnd, bool Success, bool Skipped, string Reason, int ErrorCode = 0);

public sealed record RestoreReport(IReadOnlyList<RestoreItemResult> Items)
{
    public bool Success => Items.All(item => item.Success);
    public bool HasFailures => Items.Any(item => !item.Success && !item.Skipped);
}

public sealed class StateChangedEventArgs(GhostState state) : EventArgs
{
    public GhostState State { get; } = state;
}

public enum UserErrorKind
{
    SelectionStateCaptureFailed,
    GhostActivationFailed,
    WindowPlacementCorrectionFailed,
    HideWindowFailed,
    RevealWindowFailed,
    RestoreFailed
}

public sealed class UserErrorEventArgs(UserErrorKind kind, string technicalMessage) : EventArgs
{
    public UserErrorKind Kind { get; } = kind;
    public string TechnicalMessage { get; } = technicalMessage;
}

public interface ILogger
{
    void Log(LogLevel level, string message, Exception? exception = null);
}

public sealed class NullLogger : ILogger
{
    public static readonly NullLogger Instance = new();
    private NullLogger() { }
    public void Log(LogLevel level, string message, Exception? exception = null) { }
}

public interface IWindowApi
{
    bool IsWindow(nint hwnd);
    WindowObservation? Observe(nint hwnd);
    OperationResult<WindowSnapshot> CaptureSnapshot(TargetWindow target);
    bool IsSameIdentity(WindowSnapshot snapshot, WindowObservation observation);
    Point GetCursorPosition();
}

public interface IVisibilityBackend
{
    NativeResult ApplyGhost(nint hwnd, WindowSnapshot snapshot);
    NativeResult ApplyReveal(nint hwnd, CircleRegion region, WindowSnapshot snapshot);
    NativeResult UpdateRevealRegion(nint hwnd, CircleRegion region);
    NativeResult EnsureWindowPlacement(nint hwnd, WindowSnapshot snapshot);
    NativeResult Restore(nint hwnd, WindowSnapshot snapshot);
}

public interface IRevealVisualHost
{
    bool IsAvailable { get; }
    NativeResult Prepare(RevealVisualState visual);
    NativeResult Present();
    void Hide();
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    private SystemClock() { }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
