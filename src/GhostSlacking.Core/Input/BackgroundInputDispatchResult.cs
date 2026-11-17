namespace GhostSlacking.Core;

public enum BackgroundInputCategory
{
    RawKey,
    Character,
    EditCommand,
    Modifier,
    Mouse,
    Ignored
}

public enum BackgroundInputRejectionReason
{
    DiscoveryUnavailable,
    PendingTextOperation,
    NoInputRecipient,
    ImeComposition,
    DeadKey,
    SystemShortcut,
    UnsupportedKey,
    UnsupportedMouse,
    PasteUnavailable,
    OutsideEditor,
    ControlUnavailable,
    PasteRequiresEmptyEditor,
    RichContentUnverified,
    ClipboardUnavailable
}

public readonly record struct BackgroundInputDispatchResult(
    NativeResult Result,
    BackgroundInputCategory Category,
    bool KeyDownPosted = false,
    BackgroundInputRejectionReason? Rejection = null,
    long PendingOperation = 0)
{
    public static BackgroundInputDispatchResult Pending(long operation, BackgroundInputCategory category) =>
        new(NativeResult.Ok("BackgroundInputPending"), category, PendingOperation: operation);

    public static BackgroundInputDispatchResult Accepted(BackgroundInputCategory category) =>
        new(NativeResult.Ok("BackgroundInput"), category);

    public static BackgroundInputDispatchResult Rejected(BackgroundInputRejectionReason reason) =>
        new(NativeResult.Failed("BackgroundInputRejected", 0, reason.ToString()), BackgroundInputCategory.Ignored, Rejection: reason);
}

public readonly record struct BackgroundInputCompletion(long SessionGeneration, long Operation,
    BackgroundInputDispatchResult Dispatch);

public static class BackgroundOperationDeadlines
{
    public static readonly TimeSpan Discovery = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan Mutation = TimeSpan.FromMilliseconds(500);
    public static BackgroundInputDispatchResult Timeout(bool mutationStarted) => mutationStarted
        ? new(NativeResult.Failed("BackgroundAutomationOutcomeUnknown", 0, "The issued operation timed out; do not replay it."), BackgroundInputCategory.EditCommand)
        : BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.DiscoveryUnavailable);
}
