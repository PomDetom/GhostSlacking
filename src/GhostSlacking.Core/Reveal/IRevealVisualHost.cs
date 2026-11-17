
namespace GhostSlacking.Core;

public interface IRevealVisualHost
{
    bool IsAvailable { get; }
    NativeResult Prepare(RevealVisualState visual);
    NativeResult Present();
    void Hide();
    void InvalidateGeometry() => Hide();
}
