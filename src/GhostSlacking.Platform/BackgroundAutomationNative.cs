// Slot order and signatures from Windows SDK 10.0.26100.0 UIAutomationClient.h.
// Unused slots are never invoked. SetFocus is deliberately not exposed.
using System.Runtime.InteropServices;
namespace GhostSlacking.Platform;
[StructLayout(LayoutKind.Sequential)]
internal struct UiaClientPoint { public int X, Y; public UiaClientPoint(int x, int y) { X = x; Y = y; } }
[ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationCondition { }
[ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomation
{
    [return: MarshalAs(UnmanagedType.Bool)] bool CompareElements(IUIAutomationElement first, IUIAutomationElement second);
    void Unused1();
    void Unused2();
    IUIAutomationElement ElementFromHandle(nint window);
    void Unused4();
    void Unused5();
    void Unused6();
    IUIAutomationElement ElementFromHandleBuildCache(nint window, IUIAutomationCacheRequest cache);
    IUIAutomationElement ElementFromPointBuildCache(UiaClientPoint point, IUIAutomationCacheRequest cache);
    void Unused9();
    void Unused10();
    void Unused11();
    void Unused12();
    IUIAutomationTreeWalker get_RawViewWalker();
    void Unused14();
    void Unused15();
    void Unused16();
    IUIAutomationCacheRequest CreateCacheRequest();
    IUIAutomationCondition CreateTrueCondition();
    void Unused19();
    IUIAutomationCondition CreatePropertyCondition(int property, [MarshalAs(UnmanagedType.Struct)] object value);
    void Unused21();
    void Unused22();
    void Unused23();
    void Unused24();
    void Unused25();
    void Unused26();
    void Unused27();
    IUIAutomationCondition CreateNotCondition(IUIAutomationCondition condition);
    void Unused29();
    void Unused30();
    void Unused31();
    void Unused32();
    void Unused33();
    void Unused34();
    void Unused35();
    void Unused36();
    void Unused37();
    void Unused38();
    void Unused39();
    void Unused40();
    void Unused41();
    void Unused42();
    void Unused43();
    void Unused44();
    void Unused45();
    void Unused46();
    void Unused47();
    void Unused48();
    void Unused49();
    void Unused50();
    void Unused51();
    void Unused52();
    void Unused53();
    void Unused54();
}
[ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElement
{
    void Unused0();
    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] int[] GetRuntimeId();
    void Unused2();
    void Unused3();
    IUIAutomationElement? FindFirstBuildCache(int scope, IUIAutomationCondition condition, IUIAutomationCacheRequest cache);
    void Unused5();
    void Unused6();
    [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int property);
    void Unused8();
    [return: MarshalAs(UnmanagedType.Struct)] object GetCachedPropertyValue(int property);
    void Unused10();
    void Unused11();
    void Unused12();
    [return: MarshalAs(UnmanagedType.IUnknown)] object GetCurrentPattern(int pattern);
    [return: MarshalAs(UnmanagedType.IUnknown)] object GetCachedPattern(int pattern);
    void Unused15();
    void Unused16();
    void Unused17();
    void Unused18();
    void Unused19();
    void Unused20();
    void Unused21();
    void Unused22();
    void Unused23();
    void Unused24();
    void Unused25();
    void Unused26();
    void Unused27();
    void Unused28();
    void Unused29();
    void Unused30();
    void Unused31();
    void Unused32();
    void Unused33();
    void Unused34();
    void Unused35();
    void Unused36();
    void Unused37();
    void Unused38();
    void Unused39();
    void Unused40();
    void Unused41();
    void Unused42();
    void Unused43();
    void Unused44();
    void Unused45();
    void Unused46();
    void Unused47();
    void Unused48();
    void Unused49();
    void Unused50();
    void Unused51();
    void Unused52();
    void Unused53();
    void Unused54();
    void Unused55();
    void Unused56();
    void Unused57();
    void Unused58();
    void Unused59();
    void Unused60();
    void Unused61();
    void Unused62();
    void Unused63();
    void Unused64();
    void Unused65();
    void Unused66();
    void Unused67();
    void Unused68();
    void Unused69();
    void Unused70();
    void Unused71();
    void Unused72();
    void Unused73();
    void Unused74();
    void Unused75();
    void Unused76();
    void Unused77();
    void Unused78();
    void Unused79();
    void Unused80();
    void Unused81();
}
[ComImport, Guid("b32a92b5-bc25-4078-9c08-d7ee95c48e03"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationCacheRequest
{
    void AddProperty(int property);
    void AddPattern(int pattern);
    void Unused2();
    void Unused3();
    void put_TreeScope(int scope);
    void Unused5();
    void Unused6();
    void Unused7();
    void Unused8();
}
[ComImport, Guid("4042c624-389c-4afc-a630-9df854a541fc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTreeWalker
{
    void Unused0();
    void Unused1();
    void Unused2();
    void Unused3();
    void Unused4();
    void Unused5();
    IUIAutomationElement? GetParentElementBuildCache(IUIAutomationElement element, IUIAutomationCacheRequest cache);
    void Unused7();
    void Unused8();
    void Unused9();
    void Unused10();
    void Unused11();
    void Unused12();
}
[ComImport, Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTextPattern
{
    IUIAutomationTextRange RangeFromPoint(UiaClientPoint point);
    void Unused1();
    IUIAutomationTextRangeArray GetSelection();
    void Unused3();
    IUIAutomationTextRange get_DocumentRange();
    int get_SupportedTextSelection();
}
[ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTextRange
{
    IUIAutomationTextRange Clone();
    void Unused1();
    int CompareEndpoints(int endpoint, IUIAutomationTextRange other, int otherEndpoint);
    void ExpandToEnclosingUnit(int unit);
    void Unused4();
    void Unused5();
    void Unused6();
    void Unused7();
    void Unused8();
    [return: MarshalAs(UnmanagedType.BStr)] string GetText(int maximum);
    void Unused10();
    void Unused11();
    void MoveEndpointByRange(int endpoint, IUIAutomationTextRange other, int otherEndpoint);
    void Select();
    void Unused14();
    void Unused15();
    void Unused16();
    IUIAutomationElementArray GetChildren();
}
[ComImport, Guid("ce4ae76a-e717-4c98-81ea-47371d028eb6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTextRangeArray
{
    int get_Length();
    IUIAutomationTextRange GetElement(int index);
}

[ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElementArray
{
    int get_Length();
    IUIAutomationElement GetElement(int index);
}
[ComImport, Guid("fb377fbe-8ea6-46d5-9c73-6499642d3059"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationInvokePattern { void Invoke(); }
[ComImport, Guid("94cf8058-9b8d-4ab9-8bfd-4cd0a33c8c70"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTogglePattern
{
    void Toggle();
    int get_CurrentToggleState();
}
[ComImport, Guid("619be086-1f4e-4ee4-bafa-210128738730"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationExpandCollapsePattern
{
    void Expand();
    void Collapse();
    int get_CurrentExpandCollapseState();
}
[ComImport, Guid("a8efa66a-0fda-421a-9194-38021f3578ea"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationSelectionItemPattern { void Select(); }
[ComImport, Guid("a94cd8b1-0844-4cd6-9d2d-640537ab39e9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationValuePattern
{
    void SetValue([MarshalAs(UnmanagedType.BStr)] string value);
    [return: MarshalAs(UnmanagedType.BStr)] string get_CurrentValue();
    [return: MarshalAs(UnmanagedType.Bool)] bool get_CurrentIsReadOnly();
}
