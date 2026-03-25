using System.Runtime.InteropServices;

namespace LilAgents.Interop;

/// <summary>
/// P/Invoke declarations for Win32 APIs used by the rendering engine.
/// Covers window management, GDI, layered windows, taskbar, and monitor enumeration.
/// </summary>
internal static partial class NativeMethods
{
    // ── Window class / creation ──────────────────────────────────────

    internal delegate nint WndProc(nint hWnd, uint msg, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public nint hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterClassW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        nint hInstance);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowExW(
        uint dwExStyle,
        [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial nint DefWindowProcW(nint hWnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(
        nint hWnd, nint hWndInsertAfter,
        int x, int y, int cx, int cy,
        uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint hWnd);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GetModuleHandleW(nint lpModuleName);

    // ── Messages ─────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekMessageW(
        out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll")]
    internal static partial nint DispatchMessageW(ref MSG lpMsg);

    // ── Layered window ───────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateLayeredWindow(
        nint hWnd,
        nint hdcDst,
        ref POINT pptDst,
        ref SIZE psize,
        nint hdcSrc,
        ref POINT pptSrc,
        uint crKey,
        ref BLENDFUNCTION pblend,
        uint dwFlags);

    // ── GDI ──────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        public int biSize;
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

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    internal static partial nint CreateDIBSection(
        nint hdc,
        ref BITMAPINFOHEADER pbmi,
        uint usage,
        out nint ppvBits,
        nint hSection,
        uint offset);

    [LibraryImport("gdi32.dll")]
    internal static partial nint SelectObject(nint hdc, nint h);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint ho);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(nint hdc);

    // ── Taskbar / AppBar ─────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct APPBARDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [LibraryImport("shell32.dll")]
    internal static partial nuint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    // ── Monitor enumeration ──────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    internal delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref RECT lprcMonitor, nint dwData);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayMonitors(
        nint hdc, nint lprcClip,
        MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFOEX lpmi);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    // ── DPI ──────────────────────────────────────────────────────────

    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(
        nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint hwnd);

    // ── Constants ────────────────────────────────────────────────────

    // Window styles
    internal const uint WS_POPUP = 0x80000000;
    internal const uint WS_VISIBLE = 0x10000000;

    // Extended window styles
    internal const uint WS_EX_LAYERED = 0x00080000;
    internal const uint WS_EX_TOPMOST = 0x00000008;
    internal const uint WS_EX_TOOLWINDOW = 0x00000080;
    internal const uint WS_EX_TRANSPARENT = 0x00000020;
    internal const uint WS_EX_NOACTIVATE = 0x08000000;

    // ShowWindow
    internal const int SW_HIDE = 0;
    internal const int SW_SHOWNOACTIVATE = 4;

    // SetWindowPos
    internal static readonly nint HWND_TOPMOST = new(-1);
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const uint SWP_HIDEWINDOW = 0x0080;

    // UpdateLayeredWindow
    internal const uint ULW_ALPHA = 0x00000002;

    // BLENDFUNCTION
    internal const byte AC_SRC_OVER = 0x00;
    internal const byte AC_SRC_ALPHA = 0x01;

    // Window messages
    internal const uint WM_LBUTTONDOWN = 0x0201;
    internal const uint WM_NCHITTEST = 0x0084;
    internal const uint WM_DESTROY = 0x0002;
    internal const uint WM_CLOSE = 0x0010;
    internal const uint WM_MOUSEACTIVATE = 0x0021;
    internal const uint WM_SETCURSOR = 0x0020;

    // Cursor
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint LoadCursorW(nint hInstance, int lpCursorName);

    [DllImport("user32.dll")]
    internal static extern nint SetCursor(nint hCursor);

    // WM_MOUSEACTIVATE return values
    internal const int MA_NOACTIVATE = 3;
    internal const int MA_NOACTIVATEANDEAT = 4;

    // WM_NCHITTEST return values
    internal const int HTTRANSPARENT = -1;

    // PeekMessage
    internal const uint PM_REMOVE = 0x0001;

    // AppBar messages
    internal const uint ABM_GETTASKBARPOS = 0x00000005;
    internal const uint ABM_GETSTATE = 0x00000004;

    // AppBar states
    internal const int ABS_AUTOHIDE = 0x0000001;

    // AppBar edges
    internal const uint ABE_LEFT = 0;
    internal const uint ABE_TOP = 1;
    internal const uint ABE_RIGHT = 2;
    internal const uint ABE_BOTTOM = 3;

    // Monitor
    internal const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    internal const uint MONITORINFOF_PRIMARY = 0x00000001;

    // DIB
    internal const uint DIB_RGB_COLORS = 0;
    internal const uint BI_RGB = 0;

    // DPI
    internal const int MDT_EFFECTIVE_DPI = 0;
}
