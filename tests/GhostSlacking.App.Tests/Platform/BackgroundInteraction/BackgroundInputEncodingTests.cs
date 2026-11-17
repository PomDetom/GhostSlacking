using GhostSlacking.Core;
using GhostSlacking.Platform;

namespace GhostSlacking.App.Tests;

public sealed class BackgroundInputEncodingTests
{
    [Fact]
    public void Key_message_encodes_extended_scan_code_repeat_and_release_without_pointer_sign_loss()
    {
        var down = new BackgroundKeyInput(0xA3, 0x11D, true, true, false);
        Assert.Equal(0x011D0001L, (long)Win32BackgroundInputBackend.KeyMessageParameter(down, false, false));
        Assert.Equal(0xC11D0001L, (long)Win32BackgroundInputBackend.KeyMessageParameter(down with { IsDown = false }, true, false));
        Assert.Equal(0x611D0001L, (long)Win32BackgroundInputBackend.KeyMessageParameter(down with { SystemKey = true }, true, true));
    }

    [Fact]
    public void Composition_layouts_are_not_treated_as_verified_plain_character_input()
    {
        Assert.True(Win32BackgroundInputBackend.IsCompositionLayout((nint)0x08040804));
        Assert.True(Win32BackgroundInputBackend.IsCompositionLayout((nint)0x04110411));
        Assert.True(Win32BackgroundInputBackend.IsCompositionLayout((nint)0x04120412));
        Assert.False(Win32BackgroundInputBackend.IsCompositionLayout((nint)0x04090409));
        Assert.False(Win32BackgroundInputBackend.IsCompositionLayout((nint)0x04070407));
    }

    [Fact]
    public void Sided_hook_modifier_codes_are_translated_to_window_message_modifier_codes()
    {
        foreach (var key in new[] { 0xA0, 0xA1 }) Assert.Equal(0x10, Win32BackgroundInputBackend.MessageVirtualKey(key));
        foreach (var key in new[] { 0xA2, 0xA3 }) Assert.Equal(0x11, Win32BackgroundInputBackend.MessageVirtualKey(key));
        foreach (var key in new[] { 0xA4, 0xA5 }) Assert.Equal(0x12, Win32BackgroundInputBackend.MessageVirtualKey(key));
        Assert.Equal(0x41, Win32BackgroundInputBackend.MessageVirtualKey(0x41));
    }
}
