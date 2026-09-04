using GhostSlacking.Core;

namespace GhostSlacking.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void Normalize_clamps_user_editable_values()
    {
        var settings = new AppSettings { RevealDiameterPx = 5000, PeekVirtualKey = 0 };

        var normalized = settings.Normalize();

        Assert.Equal(800, normalized.RevealDiameterPx);
        Assert.Equal(0x12, normalized.PeekVirtualKey);
    }

    [Fact]
    public void Normalize_falls_back_to_circle_for_unknown_shape()
    {
        var settings = new AppSettings { RevealShape = (RevealShape)999 };

        var normalized = settings.Normalize();

        Assert.Equal(RevealShape.Circle, normalized.RevealShape);
    }

    [Fact]
    public void Normalize_falls_back_to_hold_for_unknown_peek_trigger()
    {
        var settings = new AppSettings { PeekTrigger = (PeekTrigger)999 };

        var normalized = settings.Normalize();

        Assert.Equal(PeekTrigger.Hold, normalized.PeekTrigger);
    }

    [Fact]
    public void Normalize_falls_back_to_default_window_toggle_key()
    {
        var settings = new AppSettings { WindowToggleVirtualKey = 0 };

        var normalized = settings.Normalize();

        Assert.Equal(0x47, normalized.WindowToggleVirtualKey);
    }

    [Fact]
    public void Normalize_falls_back_to_default_global_hotkeys()
    {
        var settings = new AppSettings
        {
            PickHotkey = new(0x11, ShortcutModifiers.Control),
            RestoreHotkey = new(256, ShortcutModifiers.None),
            RestoreAllHotkey = new(0x52, (ShortcutModifiers)8),
            SettingsHotkey = new(0, ShortcutModifiers.Control),
            ExitHotkey = new(0, ShortcutModifiers.Alt)
        };

        var normalized = settings.Normalize();

        Assert.Equal(new HotkeyBinding(0x50, ShortcutModifiers.Control | ShortcutModifiers.Alt), normalized.PickHotkey);
        Assert.Equal(new HotkeyBinding(0x52, ShortcutModifiers.Control | ShortcutModifiers.Alt), normalized.RestoreHotkey);
        Assert.Equal(new HotkeyBinding(0x52, ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift), normalized.RestoreAllHotkey);
        Assert.Equal(new HotkeyBinding(0x53, ShortcutModifiers.Control | ShortcutModifiers.Alt), normalized.SettingsHotkey);
        Assert.Equal(new HotkeyBinding(0x51, ShortcutModifiers.Control | ShortcutModifiers.Alt), normalized.ExitHotkey);
    }

    [Fact]
    public void Normalize_preserves_disabled_global_hotkeys()
    {
        var settings = new AppSettings
        {
            PickHotkey = HotkeyBinding.Disabled,
            WindowToggleHotkey = HotkeyBinding.Disabled,
            RestoreHotkey = HotkeyBinding.Disabled,
            RestoreAllHotkey = HotkeyBinding.Disabled,
            SettingsHotkey = HotkeyBinding.Disabled,
            ExitHotkey = HotkeyBinding.Disabled
        };

        var normalized = settings.Normalize();

        Assert.True(normalized.PickHotkey.IsDisabled);
        Assert.True(normalized.WindowToggleHotkey.IsDisabled);
        Assert.True(normalized.RestoreHotkey.IsDisabled);
        Assert.True(normalized.RestoreAllHotkey.IsDisabled);
        Assert.True(normalized.SettingsHotkey.IsDisabled);
        Assert.True(normalized.ExitHotkey.IsDisabled);
    }

    [Theory]
    [InlineData(0x10)]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0xA0)]
    [InlineData(0xA1)]
    [InlineData(0xA2)]
    [InlineData(0xA3)]
    [InlineData(0xA4)]
    [InlineData(0xA5)]
    public void Hotkey_binding_rejects_modifier_keys_as_the_primary_key(int virtualKey)
    {
        var binding = new HotkeyBinding(virtualKey, ShortcutModifiers.Control | ShortcutModifiers.Alt);

        Assert.False(binding.IsValid());
    }
}
