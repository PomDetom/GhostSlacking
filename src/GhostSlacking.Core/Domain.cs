using System.Drawing;

namespace GhostSlacking.Core;

public enum GhostState
{
    Idle,
    Picking,
    Preparing,
    Ghost,
    Reveal,
    Restoring,
    RecoveryError
}

public enum RevealShape
{
    Circle,
    Rectangle,
    RoundedRectangle
}

public enum PeekTrigger
{
    Hold
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
    public int DiameterPx { get; init; } = 240;
    public RevealShape Shape { get; init; } = RevealShape.Circle;
    public PeekTrigger Trigger { get; init; } = PeekTrigger.Hold;

    public bool IsValid() => DiameterPx is >= 64 and <= 800 &&
        Shape is RevealShape.Circle or RevealShape.Rectangle or RevealShape.RoundedRectangle &&
        Trigger == PeekTrigger.Hold;
}

public sealed record WindowStyleSnapshot(nint Style, nint ExtendedStyle);

public sealed record WindowSnapshot
{
    public required nint Hwnd { get; init; }
    public required uint ProcessId { get; init; }
    public string? ProcessName { get; init; }
    public string? ProcessStartIdentity { get; init; }
    public required Rectangle ScreenBounds { get; init; }
    public required bool WasVisible { get; init; }
    public required bool WasMinimized { get; init; }
    public byte[]? OriginalRegionData { get; init; }
    public int? OriginalSystemBackdropType { get; init; }
    public int? OriginalNonClientRenderingPolicy { get; init; }
    public int? OriginalWindowCornerPreference { get; init; }
    public int? OriginalBorderColor { get; init; }
    public required WindowStyleSnapshot Styles { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public int SchemaVersion { get; init; } = 1;
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
    public string? ProcessName { get; init; }
}

public sealed record GhostWindowProfile(WindowSnapshot Original, RevealSettings Reveal)
{
    public nint Hwnd => Original.Hwnd;
}

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public UiLanguage Language { get; init; } = UiLanguage.Chinese;
    public int RevealDiameterPx { get; init; } = 240;
    public RevealShape RevealShape { get; init; } = RevealShape.Circle;
    public int PeekVirtualKey { get; init; } = 0x12;
    public bool StartWithWindows { get; init; }
    public bool RestoreOnExit { get; init; } = true;
    public LogLevel MinimumLogLevel { get; init; } = LogLevel.Info;

    public AppSettings Normalize() => this with
    {
        Language = Language is UiLanguage.Chinese or UiLanguage.English ? Language : UiLanguage.Chinese,
        RevealDiameterPx = Math.Clamp(RevealDiameterPx, 64, 800),
        RevealShape = RevealShape is RevealShape.Circle or RevealShape.Rectangle or RevealShape.RoundedRectangle ? RevealShape : RevealShape.Circle,
        PeekVirtualKey = PeekVirtualKey is >= 1 and <= 255 ? PeekVirtualKey : 0x12
    };
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
    NativeResult ApplyGhost(nint hwnd);
    NativeResult ApplyReveal(nint hwnd, CircleRegion region);
    NativeResult Restore(nint hwnd, WindowSnapshot snapshot);
}
