using System.Runtime.InteropServices;
using LilAgents.Interop;
using LilAgents.Themes;
using Microsoft.UI.Dispatching;

namespace LilAgents.Services;

/// <summary>
/// System tray icon with a native Win32 popup menu.
/// XAML MenuFlyout (Toggle/Radio/SubItem) crashes in tray icon hidden
/// windows regardless of library (H.NotifyIcon, WinUIEx). Pure Win32
/// TrackPopupMenu works reliably with full submenu + checkmark support.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly DispatcherQueue _mainDispatcher;
    private WinUIEx.TrayIcon? _trayIcon;
    private bool _disposed;

    // Menu item IDs
    private const int ID_BRUCE = 100;
    private const int ID_JAZZ = 101;
    private const int ID_SOUNDS = 200;
    private const int ID_THEME_PEACH = 300;
    private const int ID_THEME_MIDNIGHT = 301;
    private const int ID_THEME_CLOUD = 302;
    private const int ID_THEME_MOSS = 303;
    private const int ID_DISPLAY_AUTO = 400;
    private const int ID_DISPLAY_BASE = 401; // +index
    private const int ID_UPDATE = 500;
    private const int ID_QUIT = 999;

    public event Action<string, bool>? CharacterToggled;
    public event Action<bool>? SoundToggled;
    public event Action<PopoverTheme>? ThemeSelected;
    public event Action<int>? DisplaySelected;
    public event Action? CheckForUpdatesRequested;
    public event Action? QuitRequested;

    public TrayIconService(SettingsService settings)
    {
        _settings = settings;
        _mainDispatcher = DispatcherQueue.GetForCurrentThread();
    }

    public void Initialize()
    {
        var icoPath = GetTrayIconPath();
        _trayIcon = new WinUIEx.TrayIcon(1, icoPath, "lil agents");

        // Use ContextMenu event — we set e.Flyout to null and show our own Win32 menu.
        _trayIcon.ContextMenu += OnContextMenu;
        _trayIcon.IsVisible = true;

        // Create a small message-only window for TrackPopupMenuEx
        _msgHwnd = CreateMessageWindow();
    }

    private nint _msgHwnd;

    private void Dispatch(Action action)
    {
        _mainDispatcher.TryEnqueue(() =>
        {
            try { action(); }
            catch { }
        });
    }

    private void OnContextMenu(WinUIEx.TrayIcon sender, WinUIEx.TrayIconEventArgs e)
    {
        // Don't set e.Flyout — we show our own native menu instead.
        ShowNativeContextMenu();
    }

    private void ShowNativeContextMenu()
    {
        var hMenu = CreatePopupMenu();
        if (hMenu == 0) return;

        try
        {
            // ── Characters ─────────────────────────────────
            AppendCheckedItem(hMenu, ID_BRUCE, "Bruce", _settings.BruceVisible);
            AppendCheckedItem(hMenu, ID_JAZZ, "Jazz", _settings.JazzVisible);
            AppendSeparator(hMenu);

            // ── Sounds ─────────────────────────────────────
            AppendCheckedItem(hMenu, ID_SOUNDS, "Sounds", _settings.SoundsEnabled);
            AppendSeparator(hMenu);

            // ── Style submenu ──────────────────────────────
            var hStyleMenu = CreatePopupMenu();
            var currentTheme = _settings.CurrentThemeName;
            var themes = PopoverTheme.All;
            int[] themeIds = [ID_THEME_PEACH, ID_THEME_MIDNIGHT, ID_THEME_CLOUD, ID_THEME_MOSS];
            for (int i = 0; i < themes.Length; i++)
            {
                AppendRadioItem(hStyleMenu, themeIds[i], themes[i].Name,
                    string.Equals(themes[i].Name, currentTheme, StringComparison.OrdinalIgnoreCase));
            }
            AppendSubmenu(hMenu, hStyleMenu, "Style");

            // ── Display submenu ────────────────────────────
            var hDisplayMenu = CreatePopupMenu();
            int pinnedIndex = _settings.PinnedDisplayIndex;
            AppendRadioItem(hDisplayMenu, ID_DISPLAY_AUTO, "Auto (Primary)", pinnedIndex == -1);
            AppendSeparator(hDisplayMenu);
            try
            {
                var areas = Microsoft.UI.Windowing.DisplayArea.FindAll();
                for (int i = 0; i < areas.Count; i++)
                {
                    var area = areas[i];
                    var name = $"Display {i + 1} ({area.WorkArea.Width}\u00d7{area.WorkArea.Height})";
                    AppendRadioItem(hDisplayMenu, ID_DISPLAY_BASE + i, name, pinnedIndex == i);
                }
            }
            catch { }
            AppendSubmenu(hMenu, hDisplayMenu, "Display");

            AppendSeparator(hMenu);

            // ── Updates & Quit ─────────────────────────────
            AppendItem(hMenu, ID_UPDATE, "Check for Updates\u2026");
            AppendItem(hMenu, ID_QUIT, "Quit");

            // Show the menu at the cursor position
            GetCursorPos(out var pt);
            SetForegroundWindow(_msgHwnd);

            int cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_NONOTIFY,
                pt.X, pt.Y, _msgHwnd, 0);

            if (cmd > 0)
                HandleMenuCommand(cmd);
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    private void HandleMenuCommand(int id)
    {
        switch (id)
        {
            case ID_BRUCE:
                var bruceVis = !_settings.BruceVisible;
                Dispatch(() => { _settings.BruceVisible = bruceVis; CharacterToggled?.Invoke("Bruce", bruceVis); });
                break;
            case ID_JAZZ:
                var jazzVis = !_settings.JazzVisible;
                Dispatch(() => { _settings.JazzVisible = jazzVis; CharacterToggled?.Invoke("Jazz", jazzVis); });
                break;
            case ID_SOUNDS:
                var soundsOn = !_settings.SoundsEnabled;
                Dispatch(() => { _settings.SoundsEnabled = soundsOn; SoundToggled?.Invoke(soundsOn); });
                break;
            case ID_THEME_PEACH:
                Dispatch(() => ThemeSelected?.Invoke(PopoverTheme.Peach));
                break;
            case ID_THEME_MIDNIGHT:
                Dispatch(() => ThemeSelected?.Invoke(PopoverTheme.Midnight));
                break;
            case ID_THEME_CLOUD:
                Dispatch(() => ThemeSelected?.Invoke(PopoverTheme.Cloud));
                break;
            case ID_THEME_MOSS:
                Dispatch(() => ThemeSelected?.Invoke(PopoverTheme.Moss));
                break;
            case ID_DISPLAY_AUTO:
                Dispatch(() => { _settings.PinnedDisplayIndex = -1; DisplaySelected?.Invoke(-1); });
                break;
            case ID_UPDATE:
                Dispatch(() => CheckForUpdatesRequested?.Invoke());
                break;
            case ID_QUIT:
                Dispatch(() => QuitRequested?.Invoke());
                break;
            default:
                if (id >= ID_DISPLAY_BASE && id < ID_DISPLAY_BASE + 20)
                {
                    int idx = id - ID_DISPLAY_BASE;
                    Dispatch(() => { _settings.PinnedDisplayIndex = idx; DisplaySelected?.Invoke(idx); });
                }
                break;
        }
    }

    // ── Native menu helpers ────────────────────────────────────────

    private static void AppendItem(nint hMenu, int id, string text)
    {
        AppendMenuW(hMenu, MF_STRING, (nuint)id, text);
    }

    private static void AppendCheckedItem(nint hMenu, int id, string text, bool isChecked)
    {
        uint flags = MF_STRING | (isChecked ? MF_CHECKED : MF_UNCHECKED);
        AppendMenuW(hMenu, flags, (nuint)id, text);
    }

    private static void AppendRadioItem(nint hMenu, int id, string text, bool isChecked)
    {
        uint flags = MF_STRING | (isChecked ? MF_CHECKED : MF_UNCHECKED);
        AppendMenuW(hMenu, flags, (nuint)id, text);
        if (isChecked)
        {
            // Set radio bullet style instead of checkmark
            CheckMenuRadioItem(hMenu, (uint)id, (uint)id, (uint)id, MF_BYCOMMAND);
        }
    }

    private static void AppendSubmenu(nint hParent, nint hSub, string text)
    {
        AppendMenuW(hParent, MF_POPUP | MF_STRING, (nuint)hSub, text);
    }

    private static void AppendSeparator(nint hMenu)
    {
        AppendMenuW(hMenu, MF_SEPARATOR, 0, null);
    }

    // ── ICO file management ────────────────────────────────────────

    private static string GetTrayIconPath()
    {
        // Pick the right pre-built ICO based on taskbar theme.
        bool dark = IsTaskbarDark();
        var name = dark ? "trayicon-light.ico" : "trayicon-dark.ico";
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", name);
        if (File.Exists(path))
            return path;

        // Fallback to any ICO
        var fallback = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "trayicon-light.ico");
        if (File.Exists(fallback))
            return fallback;

        return Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "trayicon-dark.ico");
    }

    private static bool IsTaskbarDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("SystemUsesLightTheme");
            return val is int i && i == 0;
        }
        catch
        {
            return true;
        }
    }

    // ── Message-only window for TrackPopupMenu ───────────────────

    private static nint CreateMessageWindow()
    {
        // HWND_MESSAGE parent creates a message-only window (invisible, no taskbar)
        return CreateWindowExW(0, "Static", "", 0, 0, 0, 0, 0,
            (nint)(-3), // HWND_MESSAGE
            0, 0, 0);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

    // ── P/Invoke ───────────────────────────────────────────────────

    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint MF_POPUP = 0x0010;
    private const uint MF_CHECKED = 0x0008;
    private const uint MF_UNCHECKED = 0x0000;
    private const uint MF_BYCOMMAND = 0x0000;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_NONOTIFY = 0x0080;

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CheckMenuRadioItem(nint hMenu, uint first, uint last, uint check, uint flags);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(nint hMenu, uint flags, int x, int y, nint hwnd, nint lptpm);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
