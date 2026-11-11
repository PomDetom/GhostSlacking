using System.Runtime.InteropServices;
using GhostSlacking.Platform;
namespace GhostSlacking.App.Tests;

public sealed class BackgroundTextAdapterTests
{
    [Fact]
    public void Chromium_and_firefox_roots_require_semantic_text_routing()
    {
        Assert.True(Win32BackgroundInputBackend.UsesSemanticText("Chrome_WidgetWin_1"));
        Assert.True(Win32BackgroundInputBackend.UsesSemanticText("Chrome_RenderWidgetHostHWND"));
        Assert.True(Win32BackgroundInputBackend.UsesSemanticText("MozillaWindowClass"));
        Assert.False(Win32BackgroundInputBackend.UsesSemanticText("Edit"));
    }
    [Fact]
    public void Automation_client_point_matches_the_sdk_long_pair_abi()
    {
        Assert.Equal(8, Marshal.SizeOf<UiaClientPoint>());
        Assert.Equal((nint)4, Marshal.OffsetOf<UiaClientPoint>(nameof(UiaClientPoint.Y)));
    }

    [Fact]
    public void Semantic_pattern_interfaces_preserve_sdk_guids_and_vtable_slots()
    {
        Assert.Equal(new Guid("94cf8058-9b8d-4ab9-8bfd-4cd0a33c8c70"), typeof(IUIAutomationTogglePattern).GUID);
        Assert.Equal(new Guid("619be086-1f4e-4ee4-bafa-210128738730"), typeof(IUIAutomationExpandCollapsePattern).GUID);
        Assert.Equal("GetChildren", Slot<IUIAutomationTextRange>(17));
        Assert.Equal("get_DocumentRange", Slot<IUIAutomationTextPattern>(4));
        Assert.Equal("CreateNotCondition", Slot<IUIAutomation>(28));
        Assert.Equal(new[] { "SetValue", "get_CurrentValue", "get_CurrentIsReadOnly" },
            typeof(IUIAutomationValuePattern).GetMethods().OrderBy(method => method.MetadataToken).Select(method => method.Name));
        static string Slot<T>(int index) => typeof(T).GetMethods().OrderBy(method => method.MetadataToken).ElementAt(index).Name;
    }
}
