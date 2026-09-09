using GhostSlacking.Core;

namespace GhostSlacking.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void Normalize_clamps_user_editable_values()
    {
        var settings = new AppSettings
        {
            SchemaVersion = 1,
            RevealDiameterPx = 5000,
            RevealDiameterStepPx = 1,
            RevealSoftEdgeWidthPx = 500,
            PeekVirtualKey = 0
        };

        var normalized = settings.Normalize();

        Assert.Equal(4, normalized.SchemaVersion);
        Assert.Equal(800, normalized.RevealDiameterPx);
        Assert.Equal(8, normalized.RevealDiameterStepPx);
        Assert.Equal(128, normalized.RevealSoftEdgeWidthPx);
        Assert.Equal(0x12, normalized.PeekVirtualKey);
    }

    [Fact]
    public void Normalize_falls_back_to_circle_for_unknown_shape()
    {
        var settings = new AppSettings { RevealShape = (RevealShape)999 };

        var normalized = settings.Normalize();

        Assert.Equal(RevealShape.RoundedRectangle, normalized.RevealShape);
    }

    [Fact]
    public void Normalize_falls_back_to_medium_for_unknown_blur_level()
    {
        var settings = new AppSettings { RevealBlurLevel = (RevealBlurLevel)999 };

        var normalized = settings.Normalize();

        Assert.Equal(RevealBlurLevel.Low, normalized.RevealBlurLevel);
    }

    [Fact]
    public void Normalize_falls_back_to_hold_for_unknown_peek_trigger()
    {
        var settings = new AppSettings { PeekTrigger = (PeekTrigger)999 };

        var normalized = settings.Normalize();

        Assert.Equal(PeekTrigger.Toggle, normalized.PeekTrigger);
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
            ExitHotkey = new(0, ShortcutModifiers.Alt),
            RevealDiameterIncreaseHotkey = new(300, ShortcutModifiers.Control),
            RevealDiameterDecreaseHotkey = new(0x11, ShortcutModifiers.Alt)
        };

        var normalized = settings.Normalize();

        Assert.Equal(new HotkeyBinding(0x50, ShortcutModifiers.Control | ShortcutModifiers.Alt), normalized.PickHotkey);
        Assert.Equal(new HotkeyBinding(0x52, ShortcutModifiers.Control | ShortcutModifiers.Alt), normalized.RestoreHotkey);
        Assert.Equal(new HotkeyBinding(0x52, ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift), normalized.RestoreAllHotkey);
        Assert.Equal(new HotkeyBinding(0x53, ShortcutModifiers.Control | ShortcutModifiers.Alt), normalized.SettingsHotkey);
        Assert.Equal(new HotkeyBinding(0x51, ShortcutModifiers.Control | ShortcutModifiers.Alt), normalized.ExitHotkey);
        Assert.True(normalized.RevealDiameterIncreaseHotkey.IsDisabled);
        Assert.True(normalized.RevealDiameterDecreaseHotkey.IsDisabled);
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
            ExitHotkey = HotkeyBinding.Disabled,
            RevealDiameterIncreaseHotkey = HotkeyBinding.Disabled,
            RevealDiameterDecreaseHotkey = HotkeyBinding.Disabled
        };

        var normalized = settings.Normalize();

        Assert.True(normalized.PickHotkey.IsDisabled);
        Assert.True(normalized.WindowToggleHotkey.IsDisabled);
        Assert.True(normalized.RestoreHotkey.IsDisabled);
        Assert.True(normalized.RestoreAllHotkey.IsDisabled);
        Assert.True(normalized.SettingsHotkey.IsDisabled);
        Assert.True(normalized.ExitHotkey.IsDisabled);
        Assert.True(normalized.RevealDiameterIncreaseHotkey.IsDisabled);
        Assert.True(normalized.RevealDiameterDecreaseHotkey.IsDisabled);
    }

    [Fact]
    public void Schema_one_settings_receive_new_reveal_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"GhostSlacking-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
                {
                  "SchemaVersion": 1,
                  "RevealDiameterPx": 320
                }
                """);

            var settings = new SettingsStore(path).Load();

            Assert.Equal(4, settings.SchemaVersion);
            Assert.Equal(320, settings.RevealDiameterPx);
            Assert.Equal(16, settings.RevealDiameterStepPx);
            Assert.Equal(16, settings.RevealSoftEdgeWidthPx);
            Assert.Equal(RevealBlurLevel.Low, settings.RevealBlurLevel);
            Assert.Equal(RevealShape.RoundedRectangle, settings.RevealShape);
            Assert.Equal(PeekTrigger.Toggle, settings.PeekTrigger);
            Assert.True(settings.RevealDiameterIncreaseHotkey.IsDisabled);
            Assert.True(settings.RevealDiameterDecreaseHotkey.IsDisabled);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Schema_two_settings_receive_the_default_blur_level()
    {
        var path = Path.Combine(Path.GetTempPath(), $"GhostSlacking-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
                {
                  "SchemaVersion": 2,
                  "RevealSoftEdgeWidthPx": 64
                }
                """);

            var settings = new SettingsStore(path).Load();

            Assert.Equal(4, settings.SchemaVersion);
            Assert.Equal(64, settings.RevealSoftEdgeWidthPx);
            Assert.Equal(RevealBlurLevel.Low, settings.RevealBlurLevel);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void New_settings_use_the_compact_toggle_reveal_defaults()
    {
        var settings = new AppSettings();

        Assert.Equal(144, settings.RevealDiameterPx);
        Assert.Equal(16, settings.RevealDiameterStepPx);
        Assert.Equal(16, settings.RevealSoftEdgeWidthPx);
        Assert.Equal(RevealBlurLevel.Low, settings.RevealBlurLevel);
        Assert.Equal(RevealShape.RoundedRectangle, settings.RevealShape);
        Assert.Equal(PeekTrigger.Toggle, settings.PeekTrigger);
        Assert.Equal(UiThemeMode.System, settings.ThemeMode);
    }

    [Theory]
    [InlineData(UiThemeMode.System)]
    [InlineData(UiThemeMode.Light)]
    [InlineData(UiThemeMode.Dark)]
    public void Valid_theme_modes_are_preserved(UiThemeMode themeMode)
    {
        var settings = new AppSettings { ThemeMode = themeMode };

        Assert.Equal(themeMode, settings.Normalize().ThemeMode);
    }

    [Fact]
    public void Unknown_theme_mode_falls_back_to_system()
    {
        var settings = new AppSettings { ThemeMode = (UiThemeMode)999 };

        Assert.Equal(UiThemeMode.System, settings.Normalize().ThemeMode);
    }

    [Fact]
    public void Theme_mode_is_persisted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"GhostSlacking-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);

            Assert.True(store.Save(new AppSettings { ThemeMode = UiThemeMode.Dark }));

            var settings = store.Load();
            Assert.Equal(4, settings.SchemaVersion);
            Assert.Equal(UiThemeMode.Dark, settings.ThemeMode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Selected_blur_level_is_persisted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"GhostSlacking-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);

            Assert.True(store.Save(new AppSettings { RevealBlurLevel = RevealBlurLevel.High }));

            var settings = store.Load();
            Assert.Equal(4, settings.SchemaVersion);
            Assert.Equal(RevealBlurLevel.High, settings.RevealBlurLevel);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(240, 32, 1, 272)]
    [InlineData(240, 32, -1, 208)]
    [InlineData(790, 32, 1, 800)]
    [InlineData(70, 32, -1, 64)]
    [InlineData(240, 32, 0, 240)]
    public void Diameter_adjustment_uses_the_configured_step_and_clamps(
        int diameter,
        int step,
        int direction,
        int expected)
    {
        var settings = new AppSettings
        {
            RevealDiameterPx = diameter,
            RevealDiameterStepPx = step
        };

        Assert.Equal(expected, settings.AdjustRevealDiameter(direction).RevealDiameterPx);
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
