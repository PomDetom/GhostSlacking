using System.Runtime.InteropServices;
using GhostSlacking.Core;
namespace GhostSlacking.Platform;

internal static class BackgroundClipboard
{
    // Reading is permitted only from the explicit semantic Paste request. Never
    // replace the clipboard to synthesize input or include its content in logs.
    public static NativeResult ReadText(out string text)
    {
        text = "";
        if (!IsClipboardFormatAvailable(13)) return NativeResult.Failed("BackgroundClipboardFormat", 0, "Unicode text is unavailable.");
        if (!OpenClipboard(0)) return NativeResult.Failed("BackgroundClipboardBusy", Marshal.GetLastWin32Error());
        try
        {
            var memory = GetClipboardData(13);
            var size = memory == 0 ? 0UL : (ulong)GlobalSize(memory);
            if (size < 2 || size > 1024 * 1024 || (size & 1) != 0) return NativeResult.Failed("BackgroundClipboardSize", 0);
            var address = GlobalLock(memory);
            if (address == 0) return NativeResult.Failed("BackgroundClipboardMemory", Marshal.GetLastWin32Error());
            try
            {
                var content = Marshal.PtrToStringUni(address, checked((int)(size / 2))) ?? "";
                var end = content.IndexOf('\0');
                if (end < 0) return NativeResult.Failed("BackgroundClipboardTermination", 0);
                text = content[..end];
                return NativeResult.Ok("BackgroundClipboardRead");
            }
            finally { GlobalUnlock(memory); }
        }
        finally { CloseClipboard(); }
    }
    public static NativeResult CopyText(string text)
    {
        var owner = Win32NativeMethods.CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0, 0, 0, Win32NativeMethods.GetModuleHandle(null), 0);
        if (owner == 0) return NativeResult.Failed("BackgroundClipboardOwner", Marshal.GetLastWin32Error(), "Clipboard owner unavailable.");
        nint memory = 0;
        var opened = false;
        try
        {
            opened = OpenClipboard(owner);
            if (!opened) return NativeResult.Failed("BackgroundClipboardBusy", Marshal.GetLastWin32Error(), "The clipboard is busy.");
            memory = GlobalAlloc(2, checked((nuint)((text.Length + 1) * 2)));
            var address = memory == 0 ? 0 : GlobalLock(memory);
            if (address == 0) return NativeResult.Failed("BackgroundClipboardMemory", Marshal.GetLastWin32Error(), "Clipboard allocation failed.");
            try { Marshal.Copy((text + '\0').ToCharArray(), 0, address, text.Length + 1); }
            finally { GlobalUnlock(memory); }
            if (!EmptyClipboard() || SetClipboardData(13, memory) == 0)
                return NativeResult.Failed("BackgroundClipboardWrite", Marshal.GetLastWin32Error(), "Clipboard copy failed.");
            memory = 0; // Ownership passed to the system.
            return NativeResult.Ok("BackgroundSelectionCopy");
        }
        finally
        {
            if (memory != 0) GlobalFree(memory);
            if (opened) CloseClipboard();
            Win32NativeMethods.DestroyWindow(owner);
        }
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetClipboardData(uint format, nint memory);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint GetClipboardData(uint format);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nuint GlobalSize(nint memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
}
