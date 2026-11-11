namespace GhostSlacking.Platform;

internal static class BackgroundEnglishKeyboard
{
    public static nint FindLoadedUsLayout()
    {
        var count = Win32BackgroundInputNative.GetKeyboardLayoutList(0, null);
        if (count <= 0) return 0;
        var layouts = new nint[Math.Min(count, 64)];
        var returned = Win32BackgroundInputNative.GetKeyboardLayoutList(layouts.Length, layouts);
        for (var index = 0; index < Math.Min(returned, layouts.Length); index++)
        {
            // Exclude US variants (e.g. Dvorak / International) and IME handles.
            if (unchecked((uint)(long)layouts[index]) == 0x04090409) return layouts[index];
        }
        return 0;
    }

    // Windows 8+ may refuse LoadKeyboardLayout from a background process. A
    // fixed table also avoids loading/activating a layout just to translate text.
    public static bool TryTranslate(int key, bool shift, bool capsLock, out char character)
    {
        if (key is >= 0x41 and <= 0x5A)
        {
            character = (char)((shift ^ capsLock) ? key : key + 32);
            return true;
        }
        if (key is >= 0x30 and <= 0x39)
        {
            character = shift ? ")!@#$%^&*("[key - 0x30] : (char)key;
            return true;
        }
        if (key is >= 0x60 and <= 0x69)
        {
            character = (char)('0' + key - 0x60);
            return true;
        }
        var pair = key switch
        {
            0x20 => "  ",
            0x6A => "**",
            0x6B => "++",
            0x6C => ",,",
            0x6D => "--",
            0x6E => "..",
            0x6F => "//",
            0xBA => ";:",
            0xBB => "=+",
            0xBC => ",<",
            0xBD => "-_",
            0xBE => ".>",
            0xBF => "/?",
            0xC0 => "`~",
            0xDB => "[{",
            0xDC or 0xE2 => "\\|",
            0xDD => "]}",
            0xDE => "'\"",
            _ => string.Empty
        };
        character = pair.Length == 0 ? '\0' : pair[shift ? 1 : 0];
        return pair.Length != 0;
    }
}
