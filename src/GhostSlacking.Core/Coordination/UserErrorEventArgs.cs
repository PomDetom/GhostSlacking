
namespace GhostSlacking.Core;

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
