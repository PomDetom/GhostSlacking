using GhostSlacking.Core;
namespace GhostSlacking.Platform;

internal sealed class BackgroundEnglishInputEngine : IBackgroundTextInputEngine
{
    public string Name => "US-English";
    public bool SupportsComposition => false;
    public BackgroundInputDispatchResult RequestLanguageSwitch() =>
        BackgroundInputDispatchResult.Rejected(BackgroundInputRejectionReason.ImeComposition);
    public void CancelComposition() { }
}
