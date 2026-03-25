using System.Runtime.InteropServices;
using LilAgents.Core;
using LilAgents.Themes;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace LilAgents.Views;

/// <summary>
/// Chat popover window using standard WinUI 3 window chrome with
/// ExtendsContentIntoTitleBar for a custom title bar. The window uses
/// the default Win11 rounded corners (~8px) and styles inner content
/// with concentric radii so it looks intentional.
/// </summary>
public sealed partial class PopoverWindow : Window
{
    private const int DefaultWidth = 420;
    private const int DefaultHeight = 520;

    private PopoverTheme _theme = PopoverTheme.Peach;
    private IChatSession? _session;
    private bool _activatedOnce;

    public PopoverWindow()
    {
        InitializeComponent();

        // Force Light theme so controls match the popover's light styling.
        // Without this, system dark mode makes TextBox etc. dark.
        RootContainer.RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Light;

        // Custom title bar — keeps system close button visible.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        // Style the system close button to blend with the theme.
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

        // Make the window non-resizable.
        if (AppWindow.Presenter is OverlappedPresenter op)
        {
            op.IsResizable = false;
            op.IsMaximizable = false;
            op.IsMinimizable = false;
        }

        // Hide from taskbar and Alt+Tab by setting WS_EX_TOOLWINDOW.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var exStyle = GetWindowLong(hwnd, -20); // GWL_EXSTYLE
        SetWindowLong(hwnd, -20, exStyle | 0x00000080); // WS_EX_TOOLWINDOW

        // Set the DWM caption color to match the theme background.
        var bg = _theme.PopoverBg;
        uint captionColor = (uint)(bg.R | (bg.G << 8) | (bg.B << 16));
        DwmSetWindowAttribute(hwnd, 35 /* DWMWA_CAPTION_COLOR */, ref captionColor, sizeof(uint));

        // Start cloaked — invisible but rendered, no flash on first show.
        uint cloaked = 1;
        DwmSetWindowAttribute(hwnd, 13 /* DWMWA_CLOAK */, ref cloaked, sizeof(uint));

        RootContainer.KeyDown += RootContainer_KeyDown;
        Activated += OnActivated;

        // Intercept the system close button — hide instead of destroy.
        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            HidePopover();
        };

        ApplyTheme();
    }

    // ── Theme ────────────────────────────────────────────────────────

    public PopoverTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            Chat.Theme = value;
            ApplyTheme();
        }
    }

    private void ApplyTheme()
    {
        var t = _theme;

        // Window background — visible through Win11's rounded corners.
        var bg = t.PopoverBg;
        RootContainer.Background = new SolidColorBrush(
            Windows.UI.Color.FromArgb(255, bg.R, bg.G, bg.B));

        // Title bar
        var tbBg = t.TitleBarBg;
        TitleBar.Background = new SolidColorBrush(
            Windows.UI.Color.FromArgb(255, tbBg.R, tbBg.G, tbBg.B));
        TitleText.Text = t.TitleString;
        TitleText.Foreground = new SolidColorBrush(t.TitleText);
        TitleText.FontFamily = new FontFamily(t.TitleFontFamily);
        TitleText.FontSize = t.TitleFontSize;

        Separator.Background = new SolidColorBrush(t.SeparatorColor);

        Chat.Theme = t;
    }

    // ── Session binding ──────────────────────────────────────────────

    public void BindSession(IChatSession session)
    {
        UnbindSession();

        _session = session;

        session.TextReceived += OnTextReceived;
        session.TurnCompleted += OnTurnCompleted;
        session.ErrorOccurred += OnErrorOccurred;
        session.ToolUsed += OnToolUsed;
        session.ToolResultReceived += OnToolResultReceived;

        Chat.MessageSubmitted += OnMessageSubmitted;

        if (session.History.Count > 0)
            Chat.ReplayHistory(session.History);
    }

    /// <summary>
    /// Unsubscribes all session event handlers. Must be called before the
    /// window is destroyed to prevent dead handlers from blocking events
    /// on subsequent popover instances that share the same session.
    /// </summary>
    public void UnbindSession()
    {
        if (_session is not null)
        {
            _session.TextReceived -= OnTextReceived;
            _session.TurnCompleted -= OnTurnCompleted;
            _session.ErrorOccurred -= OnErrorOccurred;
            _session.ToolUsed -= OnToolUsed;
            _session.ToolResultReceived -= OnToolResultReceived;
            _session = null;
        }
    }

    // ── Positioning ──────────────────────────────────────────────────

    public void PositionNear(double screenX, double screenY)
    {
        _pendingPosition = new Windows.Foundation.Point(screenX, screenY);
        if (_isShown)
            PositionNearImmediate(screenX, screenY);
    }

    private void PositionNearImmediate(double screenX, double screenY)
    {
        var appWindow = AppWindow;
        if (appWindow is null) return;

        // AppWindow uses physical pixels. screenX/screenY are physical.
        var dpi = GetDpiScale();
        var widthPx = (int)(DefaultWidth * dpi);
        var heightPx = (int)(DefaultHeight * dpi);
        int gap = (int)(12 * dpi);

        var x = (int)screenX - widthPx / 2;
        var y = (int)screenY - heightPx - gap;

        // Clamp to screen bounds.
        var area = DisplayArea.GetFromPoint(
            new PointInt32(x + widthPx / 2, y + heightPx / 2),
            DisplayAreaFallback.Primary);
        var workArea = area.WorkArea;

        if (x < workArea.X) x = workArea.X + 8;
        if (x + widthPx > workArea.X + workArea.Width)
            x = workArea.X + workArea.Width - widthPx - 8;
        if (y < workArea.Y) y = workArea.Y + 8;
        if (y + heightPx > workArea.Y + workArea.Height)
            y = workArea.Y + workArea.Height - heightPx - 8;

        appWindow.MoveAndResize(new RectInt32(x, y, widthPx, heightPx));
    }

    /// <summary>Hides the window using DWM cloaking (invisible but still rendered).</summary>
    public event Action? PopoverHidden;

    private bool _isShown;

    public void HidePopover()
    {
        if (!_isShown) return;
        _isShown = false;
        _activatedOnce = false;

        // Cloak the window — DWM hides it from the screen and taskbar
        // but keeps the content rendered, so uncloaking has no flash.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        uint cloaked = 1;
        DwmSetWindowAttribute(hwnd, 13 /* DWMWA_CLOAK */, ref cloaked, sizeof(uint));

        PopoverHidden?.Invoke();
    }

    /// <summary>Uncloaks the window, positions it, and activates.</summary>
    public void ShowPopover()
    {
        _isShown = true;
        _activatedOnce = false;

        // Position first (while still cloaked).
        if (_pendingPosition.HasValue)
        {
            PositionNearImmediate(_pendingPosition.Value.X, _pendingPosition.Value.Y);
            _pendingPosition = null;
        }

        // Uncloak — content is already rendered, no flash.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        uint uncloaked = 0;
        DwmSetWindowAttribute(hwnd, 13 /* DWMWA_CLOAK */, ref uncloaked, sizeof(uint));

        SetForegroundWindow(hwnd);
        Activate();
        Chat.CompletePendingSwap();
        Chat.FocusInput();
        Chat.ScrollToBottom();
    }

    private Windows.Foundation.Point? _pendingPosition;

    private double GetDpiScale()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hwnd);
        return dpi / 96.0;
    }

    // ── Event handlers ───────────────────────────────────────────────

    private void OnTextReceived(string text) => Chat.AppendStreamingText(text);
    private void OnTurnCompleted() => Chat.EndStreaming();

    private void OnErrorOccurred(string msg) => Chat.AppendError(msg);

    private void OnToolUsed(string toolName, Dictionary<string, object?> input)
    {
        string? Get(string key) => input.TryGetValue(key, out var v) ? v?.ToString() : null;
        var summary = toolName switch
        {
            "Bash" => Get("command") ?? "",
            "Read" or "Edit" or "Write" => Get("file_path") ?? "",
            "Glob" or "Grep" => Get("pattern") ?? "",
            _ => Get("description")
                 ?? string.Join(", ", input.Keys.OrderBy(k => k).Take(3)),
        };
        Chat.AppendToolUse(toolName, summary);
    }

    private void OnToolResultReceived(string summary, bool isError)
        => Chat.AppendToolResult(summary, isError);

    private void OnMessageSubmitted(string text) => _session?.Send(text);

    private void RootContainer_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            HidePopover();
            e.Handled = true;
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            if (_isShown && _activatedOnce)
                HidePopover();
        }
        else
        {
            _activatedOnce = true;
            Chat.FocusInput();
        }
    }

    // ── P/Invoke ─────────────────────────────────────────────────────

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref uint value, int size);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);
}
