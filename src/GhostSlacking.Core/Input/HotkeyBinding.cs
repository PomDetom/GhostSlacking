using System.Text.Json.Serialization;

namespace GhostSlacking.Core;

[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4
}

public readonly record struct HotkeyBinding(int VirtualKey, ShortcutModifiers Modifiers)
{
    public static HotkeyBinding Disabled => new(0, ShortcutModifiers.None);

    [JsonIgnore]
    public bool IsDisabled => this == Disabled;

    public bool IsValid() => IsDisabled ||
        (VirtualKey is >= 1 and <= 255 &&
         !IsModifierVirtualKey(VirtualKey) &&
         (Modifiers & ~(ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift)) == 0);

    public static bool IsModifierVirtualKey(int virtualKey) =>
        virtualKey is 0x10 or 0x11 or 0x12 or >= 0xA0 and <= 0xA5;

    public HotkeyBinding Normalize(int defaultVirtualKey, ShortcutModifiers defaultModifiers) =>
        IsValid() ? this : new HotkeyBinding(defaultVirtualKey, defaultModifiers);
}
