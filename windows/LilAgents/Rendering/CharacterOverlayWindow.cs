using System.Runtime.InteropServices;
using LilAgents.Core;
using LilAgents.Interop;
using Windows.Foundation;

namespace LilAgents.Rendering;

/// <summary>
/// A Win32 layered window that renders an animated character sprite with
/// per-pixel alpha transparency. The window floats above the taskbar as a
/// topmost popup that does not appear in Alt+Tab and does not steal focus.
///
/// Implements <see cref="ICharacterRenderer"/> so the animation engine can
/// drive position/frame updates through a clean abstraction.
/// </summary>
internal sealed class CharacterOverlayWindow : ICharacterRenderer
{
    // ── Fields ───────────────────────────────────────────────────────

    private readonly SpriteSheet _spriteSheet;
    private readonly SpriteRenderer _renderer;
    private readonly int _displayWidth;
    private readonly int _displayHeight;

    // Win32 handles
    private nint _hwnd;
    private string? _className;

    // Must be stored as a field to prevent GC of the delegate while the
    // window is alive — the native side holds a raw function pointer.
    private readonly NativeMethods.WndProc _wndProcDelegate;

    // State
    private int _posX;
    private int _posY;
    private int _currentFrame;
    private bool _currentFlipped;
    private bool _visible;
    private bool _disposed;

    // ── ICharacterRenderer ───────────────────────────────────────────

    public bool IsVisible => _visible;
    public int FrameCount => _spriteSheet.FrameCount;
    public event Action<double, double>? Clicked;

    // ── Construction ─────────────────────────────────────────────────

    /// <summary>
    /// Creates the overlay window and its GDI rendering resources.
    /// The window starts hidden.
    /// </summary>
    /// <param name="spriteSheet">Loaded sprite sheet with pre-multiplied BGRA frames.</param>
    /// <param name="displayWidth">On-screen width in device pixels (e.g. 113).</param>
    /// <param name="displayHeight">On-screen height in device pixels (e.g. 200).</param>
    public CharacterOverlayWindow(SpriteSheet spriteSheet, int displayWidth, int displayHeight)
    {
        _spriteSheet = spriteSheet ?? throw new ArgumentNullException(nameof(spriteSheet));
        _displayWidth = displayWidth;
        _displayHeight = displayHeight;

        _renderer = new SpriteRenderer(displayWidth, displayHeight);

        // Store the delegate to prevent GC
        _wndProcDelegate = WndProc;

        _className = WindowInterop.RegisterOverlayClass(_wndProcDelegate, "LilAgents_Character");
        _hwnd = WindowInterop.CreateOverlayWindow(_className, displayWidth, displayHeight);
    }

    // ── ICharacterRenderer implementation ────────────────────────────

    public void SetPosition(double x, double y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _posX = (int)Math.Round(x);
        _posY = (int)Math.Round(y);

        if (_visible)
        {
            UpdateWindow();
        }
    }

    public void SetFrame(int frameIndex, bool flipped)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (frameIndex < 0 || frameIndex >= _spriteSheet.FrameCount)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));

        _currentFrame = frameIndex;
        _currentFlipped = flipped;

        if (_visible)
        {
            UpdateWindow();
        }
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_visible) return;

        _visible = true;
        UpdateWindow();
        WindowInterop.ShowNoActivate(_hwnd);
    }

    public void Hide()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_visible) return;

        _visible = false;
        WindowInterop.HideWindow(_hwnd);
    }

    public Rect GetBounds()
    {
        return new Rect(_posX, _posY, _displayWidth, _displayHeight);
    }

    // ── Rendering ────────────────────────────────────────────────────

    /// <summary>
    /// Renders the current frame into the DIB and calls UpdateLayeredWindow
    /// to composite the sprite onto the screen with per-pixel alpha.
    /// </summary>
    private void UpdateWindow()
    {
        _renderer.RenderFrame(_spriteSheet, _currentFrame, _currentFlipped);

        var ptDst = new NativeMethods.POINT { X = _posX, Y = _posY };
        var size = new NativeMethods.SIZE { cx = _displayWidth, cy = _displayHeight };
        var ptSrc = new NativeMethods.POINT { X = 0, Y = 0 };

        var blend = new NativeMethods.BLENDFUNCTION
        {
            BlendOp = NativeMethods.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = NativeMethods.AC_SRC_ALPHA,
        };

        NativeMethods.UpdateLayeredWindow(
            _hwnd, 0,
            ref ptDst, ref size,
            _renderer.Hdc, ref ptSrc,
            0, ref blend,
            NativeMethods.ULW_ALPHA);
    }

    // ── WndProc ──────────────────────────────────────────────────────

    private static readonly nint _arrowCursor = NativeMethods.LoadCursorW(0, 32512); // IDC_ARROW

    private nint WndProc(nint hWnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_SETCURSOR:
                NativeMethods.SetCursor(_arrowCursor);
                return 1; // handled

            case NativeMethods.WM_MOUSEACTIVATE:
                return NativeMethods.MA_NOACTIVATE;

            case NativeMethods.WM_LBUTTONDOWN:
                // Extract screen coordinates from the cursor position.
                // lParam contains client coords but since WS_POPUP at known
                // position, convert to screen coords.
                int clientX = (short)(lParam & 0xFFFF);
                int clientY = (short)((lParam >> 16) & 0xFFFF);
                double screenX = _posX + clientX;
                double screenY = _posY + clientY;
                Clicked?.Invoke(screenX, screenY);
                return 0;

            case NativeMethods.WM_CLOSE:
                // Ignore close — we control lifecycle through Dispose
                return 0;

            default:
                return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
        }
    }

    // ── Dispose ──────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _visible = false;

        _renderer.Dispose();

        if (_hwnd != 0)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = 0;
        }

        if (_className is not null)
        {
            WindowInterop.UnregisterClass(_className);
            _className = null;
        }
    }
}
