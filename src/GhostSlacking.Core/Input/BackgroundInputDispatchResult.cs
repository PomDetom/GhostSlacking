namespace GhostSlacking.Core;

public enum BackgroundInputCategory
{
    RawKey,
    Mouse,
    Ignored
}

public enum BackgroundInputRejectionReason
{
    NoInputRecipient,
    ImeComposition,
    SystemShortcut,
    UnsupportedKey
}

public readonly record struct BackgroundInputDispatchResult(
    NativeResult Result,
    BackgroundInputCategory Category,
    bool KeyDownPosted = false,
    BackgroundInputRejectionReason? Rejection = null)
{
    public static BackgroundInputDispatchResult Accepted(BackgroundInputCategory category) =>
        new(NativeResult.Ok("BackgroundInput"), category);

    public static BackgroundInputDispatchResult Rejected(BackgroundInputRejectionReason reason) =>
        new(NativeResult.Failed("BackgroundInputRejected", 0, reason.ToString()), BackgroundInputCategory.Ignored, Rejection: reason);
}
