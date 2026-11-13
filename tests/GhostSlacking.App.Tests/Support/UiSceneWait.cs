using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace GhostSlacking.App.Tests;

internal static class UiSceneWait
{
    internal static void Commit(Control control, TimeSpan budget)
    {
        control.UpdateLayout();
        var root = TopLevel.GetTopLevel(control) ?? throw new InvalidOperationException("UI probe must be attached.");
        var visual = ElementComposition.GetElementVisual(root) ?? throw new InvalidOperationException("UI probe has no composition visual.");
        var rendered = visual.Compositor.RequestCompositionBatchCommitAsync().Rendered;
        if (rendered.IsCompleted)
        {
            rendered.GetAwaiter().GetResult();
            return;
        }
        var frame = new DispatcherFrame();
        using var stop = DispatcherTimer.RunOnce(() => frame.Continue = false, budget, DispatcherPriority.Background);
        _ = rendered.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false, DispatcherPriority.Background), TaskScheduler.Default);
        Dispatcher.UIThread.PushFrame(frame);
        if (rendered.IsCompleted)
            rendered.GetAwaiter().GetResult();
        // Callers retain their original functional assertions and readiness deadlines.
    }
}
