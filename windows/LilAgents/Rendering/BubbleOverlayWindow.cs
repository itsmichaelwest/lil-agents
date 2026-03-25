using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using LilAgents.Core;
using LilAgents.Interop;
using SD = System.Drawing;
using WinColor = Windows.UI.Color;

namespace LilAgents.Rendering;

/// <summary>
/// A small Win32 layered window that renders a speech bubble with text above a
/// character. Supports "thinking" (normal border) and "completion" (green border)
/// styles. Rendered via GDI+ into a DIB section, then composited with
/// UpdateLayeredWindow for per-pixel alpha.
/// </summary>
internal sealed class BubbleOverlayWindow : IBubbleRenderer
{
    // ── Layout constants (logical pixels, scaled by DPI at render time) ──

    private const int BaseMaxBubbleWidth = 220;
    private const int BaseBubblePaddingH = 12;
    private const int BaseBubblePaddingV = 8;
    private const int BaseBorderWidth = 2;
    private const int BaseTailHeight = 8;
    private const int BaseTailWidth = 12;
    private const float BaseCornerRadius = 10f;
    private const float BaseFontSize = 11f;
    private const string FontFamily = "Segoe UI Variable";

    // DPI-scaled values (set on each Show call)
    private int MaxBubbleWidth;
    private int BubblePaddingH;
    private int BubblePaddingV;
    private int BorderWidth;
    private int TailHeight;
    private int TailWidth;
    private float CornerRadius;
    private float FontSize;

    // ── Fields ───────────────────────────────────────────────────────

    private nint _hwnd;
    private string? _className;
    private readonly NativeMethods.WndProc _wndProcDelegate;

    // Current DIB resources (recreated when bubble size changes)
    private nint _hdc;
    private nint _hBitmap;
    private nint _hOldBitmap;
    private nint _bits;
    private int _dibWidth;
    private int _dibHeight;

    private int _posX;
    private int _posY;
    private bool _visible;
    private bool _disposed;

    // ── IBubbleRenderer ──────────────────────────────────────────────

    public bool IsVisible => _visible;

    // ── Construction ─────────────────────────────────────────────────

    public BubbleOverlayWindow()
    {
        _wndProcDelegate = WndProc;
        _className = WindowInterop.RegisterOverlayClass(_wndProcDelegate, "LilAgents_Bubble");

        // Create with a small initial size; UpdateLayeredWindow will resize
        _hwnd = WindowInterop.CreateOverlayWindow(_className, 1, 1, clickThrough: true);
    }

    // ── IBubbleRenderer implementation ───────────────────────────────

    public void Show(string text, bool isCompletion, double x, double y,
                     WinColor borderColor, WinColor textColor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Scale all layout constants by DPI
        var dpiScale = WindowInterop.GetDpiScale(WindowInterop.GetPrimaryMonitor());
        MaxBubbleWidth = (int)(BaseMaxBubbleWidth * dpiScale);
        BubblePaddingH = (int)(BaseBubblePaddingH * dpiScale);
        BubblePaddingV = (int)(BaseBubblePaddingV * dpiScale);
        BorderWidth = (int)(BaseBorderWidth * dpiScale);
        TailHeight = (int)(BaseTailHeight * dpiScale);
        TailWidth = (int)(BaseTailWidth * dpiScale);
        CornerRadius = (float)(BaseCornerRadius * dpiScale);
        FontSize = (float)(BaseFontSize * dpiScale);

        // Measure text to determine bubble size
        var (bubbleWidth, bubbleHeight, textBounds) = MeasureBubble(text);

        int totalWidth = bubbleWidth;
        int totalHeight = bubbleHeight + TailHeight;

        // Recreate DIB if needed
        EnsureDib(totalWidth, totalHeight);

        // Render bubble into DIB via GDI+
        RenderBubble(text, isCompletion, bubbleWidth, bubbleHeight, totalWidth, totalHeight,
                     textBounds, borderColor, textColor);

        // Position: center horizontally above (x, y), offset up by totalHeight
        _posX = (int)Math.Round(x - totalWidth / 2.0);
        _posY = (int)Math.Round(y - totalHeight);

        // Push onto screen with UpdateLayeredWindow
        var ptDst = new NativeMethods.POINT { X = _posX, Y = _posY };
        var size = new NativeMethods.SIZE { cx = totalWidth, cy = totalHeight };
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
            _hdc, ref ptSrc,
            0, ref blend,
            NativeMethods.ULW_ALPHA);

        if (!_visible)
        {
            _visible = true;
            WindowInterop.ShowNoActivate(_hwnd);
        }
    }

    public void Hide()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_visible) return;
        _visible = false;
        WindowInterop.HideWindow(_hwnd);
    }

    // ── Measurement ──────────────────────────────────────────────────

    private (int Width, int Height, SD.SizeF TextSize) MeasureBubble(string text)
    {
        using var font = new SD.Font(FontFamily, FontSize, SD.FontStyle.Regular, SD.GraphicsUnit.Pixel);

        // Measure with max width constraint
        int maxTextWidth = MaxBubbleWidth - 2 * BubblePaddingH - 2 * BorderWidth;

        SD.SizeF textSize;
        using (var bmp = new SD.Bitmap(1, 1, PixelFormat.Format32bppArgb))
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            textSize = g.MeasureString(text, font, maxTextWidth);
        }

        int bubbleWidth = Math.Min(
            MaxBubbleWidth,
            (int)Math.Ceiling(textSize.Width) + 2 * BubblePaddingH + 2 * BorderWidth);

        int bubbleHeight = (int)Math.Ceiling(textSize.Height) + 2 * BubblePaddingV + 2 * BorderWidth;

        // Minimum size
        bubbleWidth = Math.Max(bubbleWidth, 40);
        bubbleHeight = Math.Max(bubbleHeight, 28);

        return (bubbleWidth, bubbleHeight, textSize);
    }

    // ── GDI+ Rendering ──────────────────────────────────────────────

    private void RenderBubble(
        string text, bool isCompletion,
        int bubbleWidth, int bubbleHeight,
        int totalWidth, int totalHeight,
        SD.SizeF textBounds,
        WinColor borderColor, WinColor textColor)
    {
        // Clear the DIB to transparent
        unsafe
        {
            new Span<byte>((void*)_bits, _dibWidth * _dibHeight * 4).Clear();
        }

        // Render into a System.Drawing bitmap, then copy to DIB
        using var bmp = new SD.Bitmap(totalWidth, totalHeight, PixelFormat.Format32bppArgb);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(SD.Color.Transparent);

            // Colors
            var bgColor = SD.Color.FromArgb(242, 255, 255, 255);
            var border = SD.Color.FromArgb(borderColor.A, borderColor.R, borderColor.G, borderColor.B);
            var txtColor = SD.Color.FromArgb(textColor.A, textColor.R, textColor.G, textColor.B);

            // Draw rounded rectangle body
            var bodyRect = new SD.RectangleF(
                BorderWidth / 2f,
                BorderWidth / 2f,
                bubbleWidth - BorderWidth,
                bubbleHeight - BorderWidth);

            using var bgBrush = new SD.SolidBrush(bgColor);
            using var borderPen = new SD.Pen(border, BorderWidth);
            using var path = CreateRoundedRectPath(bodyRect, CornerRadius);

            g.FillPath(bgBrush, path);
            g.DrawPath(borderPen, path);

            // Draw tail (small triangle pointing down)
            float tailCenterX = totalWidth / 2f;
            float tailTop = bubbleHeight - BorderWidth / 2f;

            var tailPoints = new SD.PointF[]
            {
                new(tailCenterX - TailWidth / 2f, tailTop),
                new(tailCenterX, tailTop + TailHeight),
                new(tailCenterX + TailWidth / 2f, tailTop),
            };

            // Fill tail
            g.FillPolygon(bgBrush, tailPoints);
            // Draw tail border edges (not the top edge, it overlaps the body)
            g.DrawLine(borderPen, tailPoints[0], tailPoints[1]);
            g.DrawLine(borderPen, tailPoints[1], tailPoints[2]);

            // Cover the gap between tail and body with a fill
            g.FillRectangle(bgBrush,
                tailCenterX - TailWidth / 2f + BorderWidth,
                tailTop - 1,
                TailWidth - 2 * BorderWidth,
                3);

            // Draw text
            using var font = new SD.Font(FontFamily, FontSize, SD.FontStyle.Regular, SD.GraphicsUnit.Pixel);
            using var textBrush = new SD.SolidBrush(txtColor);

            var textRect = new SD.RectangleF(
                BubblePaddingH + BorderWidth,
                BubblePaddingV + BorderWidth,
                bubbleWidth - 2 * BubblePaddingH - 2 * BorderWidth,
                bubbleHeight - 2 * BubblePaddingV - 2 * BorderWidth);

            var sf = new SD.StringFormat
            {
                Alignment = SD.StringAlignment.Center,
                LineAlignment = SD.StringAlignment.Center,
                Trimming = SD.StringTrimming.EllipsisCharacter,
            };

            g.DrawString(text, font, textBrush, textRect, sf);
        }

        // Copy bitmap to DIB with pre-multiplied alpha
        var rect = new SD.Rectangle(0, 0, totalWidth, totalHeight);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int byteCount = totalWidth * totalHeight * 4;
            unsafe
            {
                byte* src = (byte*)data.Scan0;
                byte* dst = (byte*)_bits;

                for (int y = 0; y < totalHeight; y++)
                {
                    byte* srcRow = src + y * data.Stride;
                    byte* dstRow = dst + y * _dibWidth * 4;

                    for (int x = 0; x < totalWidth; x++)
                    {
                        int off = x * 4;
                        byte b = srcRow[off];
                        byte gg = srcRow[off + 1];
                        byte r = srcRow[off + 2];
                        byte a = srcRow[off + 3];

                        if (a == 255)
                        {
                            dstRow[off] = b;
                            dstRow[off + 1] = gg;
                            dstRow[off + 2] = r;
                            dstRow[off + 3] = a;
                        }
                        else if (a == 0)
                        {
                            dstRow[off] = 0;
                            dstRow[off + 1] = 0;
                            dstRow[off + 2] = 0;
                            dstRow[off + 3] = 0;
                        }
                        else
                        {
                            // Pre-multiply
                            dstRow[off] = (byte)(b * a / 255);
                            dstRow[off + 1] = (byte)(gg * a / 255);
                            dstRow[off + 2] = (byte)(r * a / 255);
                            dstRow[off + 3] = a;
                        }
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// <summary>
    /// Creates a GraphicsPath for a rounded rectangle.
    /// </summary>
    private static GraphicsPath CreateRoundedRectPath(SD.RectangleF rect, float radius)
    {
        float diameter = radius * 2;
        var path = new GraphicsPath();

        // Top-left arc
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        // Top-right arc
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        // Bottom-right arc
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        // Bottom-left arc
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);

        path.CloseFigure();
        return path;
    }

    // ── DIB management ───────────────────────────────────────────────

    /// <summary>
    /// Ensures a DIB section exists that is at least the requested size.
    /// Recreates it if the current one is too small.
    /// </summary>
    private void EnsureDib(int width, int height)
    {
        if (_hdc != 0 && _dibWidth >= width && _dibHeight >= height)
            return;

        FreeDib();

        _dibWidth = width;
        _dibHeight = height;

        _hdc = NativeMethods.CreateCompatibleDC(0);
        if (_hdc == 0)
            throw new InvalidOperationException("CreateCompatibleDC failed for bubble");

        var bmi = new NativeMethods.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // top-down
            biPlanes = 1,
            biBitCount = 32,
            biCompression = NativeMethods.BI_RGB,
        };

        _hBitmap = NativeMethods.CreateDIBSection(_hdc, ref bmi, NativeMethods.DIB_RGB_COLORS, out _bits, 0, 0);
        if (_hBitmap == 0)
        {
            NativeMethods.DeleteDC(_hdc);
            _hdc = 0;
            throw new InvalidOperationException("CreateDIBSection failed for bubble");
        }

        _hOldBitmap = NativeMethods.SelectObject(_hdc, _hBitmap);
    }

    private void FreeDib()
    {
        if (_hdc != 0)
        {
            if (_hOldBitmap != 0)
                NativeMethods.SelectObject(_hdc, _hOldBitmap);
            NativeMethods.DeleteDC(_hdc);
            _hdc = 0;
        }

        if (_hBitmap != 0)
        {
            NativeMethods.DeleteObject(_hBitmap);
            _hBitmap = 0;
        }

        _hOldBitmap = 0;
        _bits = 0;
    }

    // ── WndProc ──────────────────────────────────────────────────────

    private nint WndProc(nint hWnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_MOUSEACTIVATE:
                return NativeMethods.MA_NOACTIVATE;

            case NativeMethods.WM_NCHITTEST:
                // Fully click-through
                return NativeMethods.HTTRANSPARENT;

            case NativeMethods.WM_CLOSE:
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

        FreeDib();

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
