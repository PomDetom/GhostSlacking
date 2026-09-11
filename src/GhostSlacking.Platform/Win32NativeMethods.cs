using System.Runtime.InteropServices;

namespace GhostSlacking.Platform;

internal static class Win32NativeMethods
{
    internal const uint GA_ROOT = 2;
    internal const int GWL_STYLE = -16;
    internal const int GWL_EXSTYLE = -20;
    internal const nint WS_CAPTION = 0x00C00000;
    internal const nint WS_THICKFRAME = 0x00040000;
    internal const nint WS_MINIMIZEBOX = 0x00020000;
    internal const nint WS_MAXIMIZEBOX = 0x00010000;
    internal const nint WS_SYSMENU = 0x00080000;
    internal const nint WS_DLGFRAME = 0x00400000;
    internal const nint WS_EX_TOOLWINDOW = 0x00000080;
    internal const nint WS_EX_APPWINDOW = 0x00040000;
    internal const nint WS_EX_TOPMOST = 0x00000008;
    internal const int SW_HIDE = 0;
    internal const int SW_SHOWNORMAL = 1;
    internal const int SW_SHOWMINIMIZED = 2;
    internal const int SW_SHOWMAXIMIZED = 3;
    internal const int SW_MINIMIZE = 6;
    internal const int SW_RESTORE = 9;
    internal const int SW_SHOWNOACTIVATE = 4;
    internal const int SW_SHOWNA = 8;
    internal const int WH_KEYBOARD_LL = 13;
    internal const int WH_MOUSE_LL = 14;
    internal const int WM_HOTKEY = 0x0312;
    internal const int WM_CLOSE = 0x0010;
    internal const int WM_DISPLAYCHANGE = 0x007E;
    internal const int WM_ERASEBKGND = 0x0014;
    internal const int WM_NCDESTROY = 0x0082;
    internal const int WM_NCHITTEST = 0x0084;
    internal const int HTTRANSPARENT = -1;
    internal const int WM_LBUTTONDOWN = 0x0201;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_KEYUP = 0x0101;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_SYSKEYUP = 0x0105;
    internal const uint RDW_INVALIDATE = 0x0001;
    internal const uint RDW_ERASE = 0x0004;
    internal const uint RDW_UPDATENOW = 0x0100;
    internal const uint RDW_FRAME = 0x0400;
    internal const int ERRORREGION = 0;
    internal const int NULLREGION = 1;
    internal const int RGN_DIFF = 4;
    internal const int ERROR_CLASS_ALREADY_EXISTS = 1410;
    internal const uint WS_POPUP = 0x80000000;
    internal const uint WS_EX_TRANSPARENT = 0x00000020;
    internal const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    internal const uint WS_EX_NOACTIVATE = 0x08000000;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_FRAMECHANGED = 0x0020;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const int VK_MENU = 0x12;
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_NOREPEAT = 0x4000;
    internal const uint ERROR_SUCCESS = 0;
    internal const uint MONITOR_DEFAULTTONEAREST = 2;
    internal const int ENUM_CURRENT_SETTINGS = -1;
    internal const uint DWMWA_SYSTEMBACKDROP_TYPE = 38;
    internal const uint DWMWA_NCRENDERING_POLICY = 2;
    internal const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const uint DWMWA_BORDER_COLOR = 34;
    internal const int DWMNCRP_USEWINDOWSTYLE = 0;
    internal const int DWMNCRP_DISABLED = 1;
    internal const int DWM_WINDOW_CORNER_DONOTROUND = 1;
    internal const int DWM_COLOR_NONE = -2;
    internal static readonly nint HWND_TOPMOST = -1;
    internal static readonly nint HWND_NOTOPMOST = -2;
    internal static readonly nint HWND_MESSAGE = -3;
    internal const int DWM_SYSTEMBACKDROP_AUTO = 0;
    internal const int DWM_SYSTEMBACKDROP_NONE = 1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX
    {
        public uint Size;
        public RECT Monitor;
        public RECT WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINTL
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public POINTL Position;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;

        public ushort LogPixels;
        public uint BitsPerPixel;
        public uint PixelsWidth;
        public uint PixelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint IcmMethod;
        public uint IcmIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPLACEMENT
    {
        public uint Length;
        public uint Flags;
        public uint ShowCmd;
        public POINT MinPosition;
        public POINT MaxPosition;
        public RECT NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nint DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public POINT Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint DwExtraInfo;
    }

    internal delegate nint HookProc(int nCode, nint wParam, nint lParam);
    internal delegate nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEX
    {
        public uint Size;
        public uint Style;
        public WindowProc WindowProcedure;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint BackgroundBrush;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MONITORINFOEX monitorInfo);

    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplaySettingsEx(
        string deviceName,
        int modeNumber,
        ref DEVMODE deviceMode,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hwnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint hwnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsZoomed(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowText(nint hwnd, [Out] char[] text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetClassName(nint hwnd, [Out] char[] className, int maxCount);

    [DllImport("user32.dll")]
    internal static extern nint GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowRgn(nint hwnd, nint region);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateEllipticRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int CombineRgn(nint destination, nint source1, nint source2, int mode);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int GetRegionData(nint region, uint count, nint regionData);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint ExtCreateRegion(nint transform, uint count, nint regionData);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint objectHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint hwnd, int id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RedrawWindow(nint hwnd, nint updateRect, nint updateRegion, uint flags);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int GetRgnBox(nint region, out RECT rect);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("kernel32.dll")]
    internal static extern void SetLastError(uint errorCode);

    [DllImport("dwmapi.dll", SetLastError = true)]
    internal static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out int value, uint valueSize);

    [DllImport("dwmapi.dll", SetLastError = true)]
    internal static extern int DwmSetWindowAttribute(nint hwnd, uint attribute, ref int value, uint valueSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(int idHook, HookProc callback, nint moduleHandle, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hookHandle);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hookHandle, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);

    internal static int LastError => Marshal.GetLastWin32Error();
}
