using System.Runtime.InteropServices;

namespace LilAgents.Interop;

/// <summary>
/// Helper utilities for creating and managing Win32 overlay windows (HWND).
/// Provides a higher-level API over raw P/Invoke calls.
/// </summary>
internal static class WindowInterop
{
    private static int _classCounter;

    /// <summary>
    /// Registers a unique Win32 window class with the given WndProc.
    /// Returns the class name. The caller must call <see cref="UnregisterClass"/>
    /// when finished.
    /// </summary>
    internal static string RegisterOverlayClass(NativeMethods.WndProc wndProc, string prefix = "LilAgents_Overlay")
    {
        var className = $"{prefix}_{Interlocked.Increment(ref _classCounter)}";
        var hInstance = NativeMethods.GetModuleHandleW(0);

        var wcex = new NativeMethods.WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
            hInstance = hInstance,
            lpszClassName = className,
        };

        var atom = NativeMethods.RegisterClassExW(ref wcex);
        if (atom == 0)
        {
            throw new InvalidOperationException(
                $"RegisterClassExW failed for '{className}': {Marshal.GetLastPInvokeError()}");
        }

        return className;
    }

    /// <summary>
    /// Creates an overlay window suitable for layered rendering.
    /// The window is created hidden (not shown).
    /// </summary>
    internal static nint CreateOverlayWindow(
        string className,
        int width,
        int height,
        bool clickThrough = false)
    {
        var hInstance = NativeMethods.GetModuleHandleW(0);

        uint exStyle = NativeMethods.WS_EX_LAYERED
                     | NativeMethods.WS_EX_TOPMOST
                     | NativeMethods.WS_EX_TOOLWINDOW
                     | NativeMethods.WS_EX_NOACTIVATE;

        if (clickThrough)
        {
            exStyle |= NativeMethods.WS_EX_TRANSPARENT;
        }

        var hwnd = NativeMethods.CreateWindowExW(
            exStyle,
            className,
            string.Empty,
            NativeMethods.WS_POPUP,
            0, 0, width, height,
            0, 0, hInstance, 0);

        if (hwnd == 0)
        {
            throw new InvalidOperationException(
                $"CreateWindowExW failed: {Marshal.GetLastPInvokeError()}");
        }

        return hwnd;
    }

    /// <summary>
    /// Unregisters a previously registered window class.
    /// </summary>
    internal static void UnregisterClass(string className)
    {
        var hInstance = NativeMethods.GetModuleHandleW(0);
        NativeMethods.UnregisterClassW(className, hInstance);
    }

    /// <summary>
    /// Shows the window without activating it or stealing focus.
    /// </summary>
    internal static void ShowNoActivate(nint hwnd)
    {
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Hides the window.
    /// </summary>
    internal static void HideWindow(nint hwnd)
    {
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
    }

    /// <summary>
    /// Moves the window to the specified screen position without activating it.
    /// </summary>
    internal static void MoveWindow(nint hwnd, int x, int y, int width, int height)
    {
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            x, y, width, height,
            NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Gets the effective DPI scale factor for a monitor (1.0 = 96 DPI).
    /// Falls back to 1.0 if the call fails.
    /// </summary>
    internal static double GetDpiScale(nint hMonitor)
    {
        int hr = NativeMethods.GetDpiForMonitor(
            hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _);
        if (hr != 0) return 1.0;
        return dpiX / 96.0;
    }

    /// <summary>
    /// Gets the DPI scale for the monitor nearest to the given point.
    /// </summary>
    internal static double GetDpiScaleForPoint(int x, int y)
    {
        var pt = new NativeMethods.POINT { X = x, Y = y };
        var hMon = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        return GetDpiScale(hMon);
    }

    /// <summary>
    /// Gets the primary monitor handle.
    /// </summary>
    internal static nint GetPrimaryMonitor()
    {
        var pt = new NativeMethods.POINT { X = 0, Y = 0 };
        return NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
    }
}
