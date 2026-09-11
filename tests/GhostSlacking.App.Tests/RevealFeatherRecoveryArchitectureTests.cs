namespace GhostSlacking.App.Tests;

public sealed class RevealFeatherRecoveryArchitectureTests
{
    [Fact]
    public void Prepare_and_present_failures_start_recovery()
    {
        var source = ReadOverlaySource();
        var prepare = Slice(source, "public NativeResult Prepare", "public NativeResult Present");
        var present = Slice(source, "public NativeResult Present", "void IRevealVisualHost.Hide");

        Assert.Contains("BeginRecovery", prepare, StringComparison.Ordinal);
        Assert.Contains("BeginRecovery", present, StringComparison.Ordinal);
    }

    [Fact]
    public void Device_loss_and_rendering_device_replacement_start_recovery()
    {
        var source = ReadOverlaySource();

        Assert.Contains("_canvasDevice.DeviceLost += OnCanvasDeviceLost", source, StringComparison.Ordinal);
        Assert.Contains("_graphicsDevice.RenderingDeviceReplaced += OnRenderingDeviceReplaced", source, StringComparison.Ordinal);
        Assert.Contains("BeginRecovery(\"The graphics device was lost.\")", source, StringComparison.Ordinal);
        Assert.Contains("BeginRecovery(\"The composition rendering device was replaced.\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_canvas_device_is_detached_but_not_disposed()
    {
        var source = ReadOverlaySource();
        var release = Slice(source, "private void ReleaseDeviceResources", "private static byte[] CreateMaskPixels");

        Assert.Contains("_canvasDevice.DeviceLost -= OnCanvasDeviceLost", release, StringComparison.Ordinal);
        Assert.DoesNotContain("_canvasDevice.Dispose", release, StringComparison.Ordinal);
    }

    [Fact]
    public void Target_changes_recreate_the_owned_overlay_before_composition_initialization()
    {
        var source = ReadOverlaySource();
        var prepare = Slice(source, "public NativeResult Prepare", "public NativeResult Present");
        var ensureWindow = Slice(source, "private void EnsureWindowForTarget", "private void InitializeComposition");

        Assert.Contains("EnsureWindowForTarget(visual.TargetHwnd);", prepare, StringComparison.Ordinal);
        Assert.Contains("window.OwnerHwnd == targetHwnd", ensureWindow, StringComparison.Ordinal);
        Assert.Contains("ReleaseCompositionResources();", ensureWindow, StringComparison.Ordinal);
        Assert.Contains("new Win32OverlayWindow(targetHwnd)", ensureWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void Graphics_recovery_rebinds_the_overlay_to_the_last_target()
    {
        var source = ReadOverlaySource();
        var recover = Slice(source, "private void TryRecover", "private static bool IsCompositionFailure");

        Assert.Contains("EnsureWindowForTarget(visual.TargetHwnd);", recover, StringComparison.Ordinal);
        Assert.Contains("InitializeComposition();", recover, StringComparison.Ordinal);
        Assert.Contains("PrepareCore(visual)", recover, StringComparison.Ordinal);
    }

    private static string ReadOverlaySource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GhostSlacking.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(
            directory.FullName,
            "src",
            "GhostSlacking.App",
            "RevealEdgeOverlay.cs"));
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);
        return source[start..end];
    }
}
