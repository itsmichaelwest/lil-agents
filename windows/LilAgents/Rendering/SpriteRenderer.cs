using System.Runtime.InteropServices;
using LilAgents.Interop;

namespace LilAgents.Rendering;

/// <summary>
/// Blits a sprite frame from a <see cref="SpriteSheet"/> into a GDI DIB section,
/// applying scale and optional horizontal flip. The resulting DC can be passed
/// directly to UpdateLayeredWindow.
/// </summary>
internal sealed class SpriteRenderer : IDisposable
{
    private nint _hdc;
    private nint _hBitmap;
    private nint _hOldBitmap;
    private nint _bits;
    private bool _disposed;

    /// <summary>Display width of the rendered sprite in pixels.</summary>
    public int DisplayWidth { get; }

    /// <summary>Display height of the rendered sprite in pixels.</summary>
    public int DisplayHeight { get; }

    /// <summary>The memory DC containing the current frame.</summary>
    public nint Hdc => _hdc;

    /// <summary>
    /// Creates a renderer targeting the given display size.
    /// A DIB section is allocated once and reused across frame updates.
    /// </summary>
    public SpriteRenderer(int displayWidth, int displayHeight)
    {
        DisplayWidth = displayWidth;
        DisplayHeight = displayHeight;

        _hdc = NativeMethods.CreateCompatibleDC(0);
        if (_hdc == 0)
            throw new InvalidOperationException("CreateCompatibleDC failed");

        var bmi = new NativeMethods.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
            biWidth = displayWidth,
            biHeight = -displayHeight, // top-down DIB
            biPlanes = 1,
            biBitCount = 32,
            biCompression = NativeMethods.BI_RGB,
        };

        _hBitmap = NativeMethods.CreateDIBSection(_hdc, ref bmi, NativeMethods.DIB_RGB_COLORS, out _bits, 0, 0);
        if (_hBitmap == 0)
        {
            NativeMethods.DeleteDC(_hdc);
            throw new InvalidOperationException("CreateDIBSection failed");
        }

        _hOldBitmap = NativeMethods.SelectObject(_hdc, _hBitmap);
    }

    /// <summary>
    /// Renders the specified sprite frame into the DIB section, scaling from
    /// native resolution to display size with nearest-neighbor interpolation.
    /// </summary>
    /// <param name="sheet">The sprite sheet containing frame data.</param>
    /// <param name="frameIndex">Zero-based index of the frame to render.</param>
    /// <param name="flipped">If true, the frame is horizontally mirrored.</param>
    public void RenderFrame(SpriteSheet sheet, int frameIndex, bool flipped)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var frameData = sheet.GetFrame(frameIndex);
        int srcW = sheet.NativeWidth;
        int srcH = sheet.NativeHeight;
        int dstW = DisplayWidth;
        int dstH = DisplayHeight;

        // Scale factors (fixed-point for speed: 16.16)
        int xRatio = (srcW << 16) / dstW;
        int yRatio = (srcH << 16) / dstH;

        unsafe
        {
            byte* dst = (byte*)_bits;
            int dstStride = dstW * 4;

            fixed (byte* srcBase = frameData)
            {
                for (int dy = 0; dy < dstH; dy++)
                {
                    int sy = (dy * yRatio) >> 16;
                    byte* dstRow = dst + dy * dstStride;
                    byte* srcRow = srcBase + sy * srcW * 4;

                    if (flipped)
                    {
                        for (int dx = 0; dx < dstW; dx++)
                        {
                            int sx = ((dstW - 1 - dx) * xRatio) >> 16;
                            int srcOff = sx * 4;
                            int dstOff = dx * 4;

                            // Copy 4 bytes (BGRA) at once
                            *(int*)(dstRow + dstOff) = *(int*)(srcRow + srcOff);
                        }
                    }
                    else
                    {
                        for (int dx = 0; dx < dstW; dx++)
                        {
                            int sx = (dx * xRatio) >> 16;
                            int srcOff = sx * 4;
                            int dstOff = dx * 4;

                            *(int*)(dstRow + dstOff) = *(int*)(srcRow + srcOff);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Clears the DIB section to fully transparent.
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        unsafe
        {
            new Span<byte>((void*)_bits, DisplayWidth * DisplayHeight * 4).Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

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
    }
}
