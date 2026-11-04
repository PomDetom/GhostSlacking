namespace GhostSlacking.Core;

// Composition engines are session-local. A provider must never change A's
// keyboard layout or borrow A's text context to produce text for B.
public interface IBackgroundTextInputEngine
{
    string Name { get; }
    bool SupportsComposition { get; }
    BackgroundInputDispatchResult RequestLanguageSwitch();
    void CancelComposition();
}
