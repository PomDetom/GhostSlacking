namespace GhostSlacking.Platform;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = Win32NativeMethods.MOD_ALT,
    Control = Win32NativeMethods.MOD_CONTROL,
    Shift = Win32NativeMethods.MOD_SHIFT,
    NoRepeat = Win32NativeMethods.MOD_NOREPEAT
}

public sealed class Win32HotkeyManager : IDisposable
{
    private readonly nint _window;
    private readonly HashSet<int> _registered = new();

    public Win32HotkeyManager(nint window)
    {
        _window = window;
    }

    public bool Register(int id, HotkeyModifiers modifiers, int virtualKey, out int errorCode)
    {
        if (Win32NativeMethods.RegisterHotKey(_window, id, (uint)modifiers, (uint)virtualKey))
        {
            _registered.Add(id);
            errorCode = 0;
            return true;
        }

        errorCode = Win32NativeMethods.LastError;
        return false;
    }

    public void Dispose()
    {
        foreach (var id in _registered)
        {
            Win32NativeMethods.UnregisterHotKey(_window, id);
        }

        _registered.Clear();
    }
}
