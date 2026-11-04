using System.Runtime.InteropServices;

namespace GhostSlacking.Platform;

internal static class Win32BackgroundInputNative
{
    [DllImport("imm32.dll")]
    internal static extern nint ImmGetDefaultIMEWnd(nint hwnd);
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hwnd);
    internal const uint EventSystemForeground = 3;
    internal delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint threadId, uint time);
    internal delegate bool EnumWindowProc(nint hwnd, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumChildWindows(nint parent, EnumWindowProc callback, nint parameter);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassName(nint hwnd, [Out] char[] className, int size);

    [StructLayout(LayoutKind.Sequential)]
    internal struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public nint Active;
        public nint Focus;
        public nint Capture;
        public nint MenuOwner;
        public nint MoveSize;
        public nint Caret;
        public Win32NativeMethods.RECT CaretRect;
    }

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsChild(nint parent, nint child);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowEnabled(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern nint GetLastActivePopup(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern nint ChildWindowFromPointEx(nint parent, Win32NativeMethods.POINT point, uint flags);

    [DllImport("user32.dll")]
    internal static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW", SetLastError = true)]
    internal static extern nint GetClassLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ScreenToClient(nint hwnd, ref Win32NativeMethods.POINT point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(nint hwnd, ref Win32NativeMethods.POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowUnicode(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint SendTextTimeout(nint hwnd, uint message, nint first, string text, uint flags, uint timeout, out nint result);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern nint GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll")]
    internal static extern int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    internal static extern nint SendMessageTimeout(nint hwnd, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [DllImport("user32.dll")]
    internal static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] state, [Out] char[] buffer, int size, uint flags, nint layout);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWinEventHook(uint minimum, uint maximum, nint module, WinEventProc callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hook);
}
