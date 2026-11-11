namespace GhostSlacking.Core;

public sealed record NotificationHudSettings
{
    public double CornerRadius { get; init; } = 10;
    public double Width { get; init; } = 132;
    public double Height { get; init; } = 44;
    public double HorizontalPosition { get; init; } = 50;
    public double VerticalPosition { get; init; } = 100;

    public NotificationHudSettings Normalize() => this with
    {
        CornerRadius = FiniteClamp(CornerRadius, 0, 48, 10),
        Width = FiniteClamp(Width, 100, 300, 132),
        Height = FiniteClamp(Height, 32, 96, 44),
        HorizontalPosition = FiniteClamp(HorizontalPosition, 0, 100, 50),
        VerticalPosition = FiniteClamp(VerticalPosition, 0, 100, 100)
    };

    private static double FiniteClamp(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}
