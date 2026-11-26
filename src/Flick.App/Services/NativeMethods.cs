using System.Runtime.InteropServices;

namespace Flick.App.Services;

internal static unsafe partial class NativeMethods
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_MBUTTONUP = 0x0208;
    public const int WM_XBUTTONDOWN = 0x020B;
    public const int WM_XBUTTONUP = 0x020C;
    public const int WM_QUIT = 0x0012;
    public const int WM_USER = 0x0400;
    public const int WM_COMMAND = 0x0111;
    public const int WM_DESTROY = 0x0002;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WM_RBUTTONUP_TRAY = 0x0205;

    public const uint LLMHF_INJECTED = 0x00000001;
    public const uint LLKHF_INJECTED = 0x00000010;
    public const uint LLKHF_EXTENDED = 0x00000001;

    public const int INPUT_MOUSE = 0;
    public const int INPUT_KEYBOARD = 1;

    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    public const uint MOUSEEVENTF_XDOWN = 0x0080;
    public const uint MOUSEEVENTF_XUP = 0x0100;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MOUSEEVENTF_HWHEEL = 0x1000;

    public const int SW_MINIMIZE = 6;
    public const int SW_MAXIMIZE = 3;
    public const int SW_RESTORE = 9;
    public const uint WM_CLOSE = 0x0010;

    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;
    public const uint SRCCOPY = 0x00CC0020;

    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_NOACTIVATE = 0x08000000;

    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_HIDE = 0;
    public const uint ULW_ALPHA = 0x00000002;
    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;

    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;
    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;

    public const uint MF_STRING = 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_RIGHTBUTTON = 0x0002;

    public delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);
    public delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);
    public delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public int type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(POINT point);

    public const uint GA_ROOT = 2;

    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(nint hWnd, char[] lpString, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UpdateLayeredWindow(
        nint hWnd, nint hdcDst, ref POINT pptDst, ref SIZE psize,
        nint hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    public static extern nint SelectObject(nint hdc, nint h);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(nint ho);

    [DllImport("gdi32.dll")]
    public static extern nint CreateDIBSection(
        nint hdc, ref BITMAPINFOHEADER pbmi, uint usage, out nint ppvBits, nint hSection, uint offset);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BitBlt(
        nint hdcDest, int xDest, int yDest, int width, int height,
        nint hdcSrc, int xSrc, int ySrc, uint rop);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    public static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AppendMenu(nint hMenu, uint uFlags, nuint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    public static extern uint TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    public static extern nint LoadIcon(nint hInstance, nint lpIconName);

    public const uint IMAGE_ICON = 1;
    public const uint LR_LOADFROMFILE = 0x00000010;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint fuLoad);

    // GDI+ flat API for smooth anti-aliased gesture trails
    [StructLayout(LayoutKind.Sequential)]
    public struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public nint DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PointF
    {
        public float X;
        public float Y;
    }

    [DllImport("gdiplus.dll")]
    public static extern int GdiplusStartup(out nuint token, ref GdiplusStartupInput input, nint output);

    [DllImport("gdiplus.dll")]
    public static extern void GdiplusShutdown(nuint token);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreateFromHDC(nint hdc, out nint graphics);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeleteGraphics(nint graphics);

    [DllImport("gdiplus.dll")]
    public static extern int GdipSetSmoothingMode(nint graphics, int smoothingMode);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreatePen1(uint color, float width, int unit, out nint pen);

    [DllImport("gdiplus.dll")]
    public static extern int GdipSetPenStartCap(nint pen, int startCap);

    [DllImport("gdiplus.dll")]
    public static extern int GdipSetPenEndCap(nint pen, int endCap);

    [DllImport("gdiplus.dll")]
    public static extern int GdipSetPenLineJoin(nint pen, int lineJoin);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeletePen(nint pen);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDrawCurve2(nint graphics, nint pen, PointF[] points, int count, float tension);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDrawLines(nint graphics, nint pen, PointF[] points, int count);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreateSolidFill(uint color, out nint brush);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeleteBrush(nint brush);

    [DllImport("gdiplus.dll")]
    public static extern int GdipFillEllipse(nint graphics, nint brush, float x, float y, float width, float height);

    [StructLayout(LayoutKind.Sequential)]
    public struct RectF
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;
    }

    [DllImport("gdiplus.dll")]
    public static extern int GdipDrawEllipse(nint graphics, nint pen, float x, float y, float width, float height);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreatePath(int brushMode, out nint path);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeletePath(nint path);

    [DllImport("gdiplus.dll")]
    public static extern int GdipAddPathArc(nint path, float x, float y, float width, float height, float startAngle, float sweepAngle);

    [DllImport("gdiplus.dll")]
    public static extern int GdipClosePathFigure(nint path);

    [DllImport("gdiplus.dll")]
    public static extern int GdipFillPath(nint graphics, nint brush, nint path);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDrawPath(nint graphics, nint pen, nint path);

    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    public static extern int GdipCreateFontFamilyFromName(string name, nint fontCollection, out nint fontFamily);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeleteFontFamily(nint fontFamily);

    [DllImport("gdiplus.dll")]
    public static extern int GdipCreateFont(nint fontFamily, float emSize, int style, int unit, out nint font);

    [DllImport("gdiplus.dll")]
    public static extern int GdipDeleteFont(nint font);

    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    public static extern int GdipMeasureString(
        nint graphics, string str, int length, nint font, ref RectF layoutRect, nint stringFormat,
        out RectF boundingBox, out int codepointsFitted, out int linesFilled);

    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    public static extern int GdipDrawString(
        nint graphics, string str, int length, nint font, ref RectF layoutRect, nint stringFormat, nint brush);
}
