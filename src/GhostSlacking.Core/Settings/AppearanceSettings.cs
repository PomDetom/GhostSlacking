
namespace GhostSlacking.Core;

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

public enum NotificationStyle
{
    Card,
    Capsule,
    Chat
}

public readonly record struct NotificationPlacement(double X, double Y)
{
    public NotificationPlacement Normalize() => new(
        double.IsFinite(X) ? Math.Clamp(X, 0, 1) : 1,
        double.IsFinite(Y) ? Math.Clamp(Y, 0, 1) : 1);
}
