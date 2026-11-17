using System.Drawing;

namespace GhostSlacking.Core;

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

public enum PeekFrameRateLimit
{
    Auto,
    Fps60,
    Fps90,
    Fps120
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
