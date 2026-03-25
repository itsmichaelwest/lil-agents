using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LilAgents.Rendering;

/// <summary>
/// Stores character animation frames as pre-multiplied BGRA byte arrays.
/// Loads from a sprite atlas PNG (single image with frames in a grid).
/// </summary>
internal sealed class SpriteSheet : IDisposable
{
    private readonly byte[][] _frames;
    private bool _disposed;

    public int FrameCount => _frames.Length;
    public int NativeWidth { get; }
    public int NativeHeight { get; }

    internal SpriteSheet(byte[][] frames, int width, int height)
    {
        _frames = frames;
        NativeWidth = width;
        NativeHeight = height;
    }

    /// <summary>
    /// Loads frames from a sprite atlas PNG.
    /// The atlas is a grid of frames (e.g., 16 columns × 16 rows).
    /// Frame dimensions are inferred from the grid size and total frame count.
    /// </summary>
    /// <param name="atlasPath">Path to the atlas PNG file.</param>
    /// <param name="columns">Number of columns in the grid.</param>
    /// <param name="totalFrames">Total number of frames (remaining grid slots are blank).</param>
    public static SpriteSheet LoadAtlas(string atlasPath, int columns = 16, int totalFrames = 241)
    {
        var path = Path.IsPathRooted(atlasPath)
            ? atlasPath
            : Path.Combine(AppContext.BaseDirectory, atlasPath);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Sprite atlas not found: {path}");

        using var atlas = new Bitmap(path);

        int frameWidth = atlas.Width / columns;
        int rows = (totalFrames + columns - 1) / columns;
        int frameHeight = atlas.Height / rows;

        // Lock the entire atlas for fast pixel access
        var rect = new Rectangle(0, 0, atlas.Width, atlas.Height);
        var atlasData = atlas.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            int atlasStride = atlasData.Stride;
            var frames = new byte[totalFrames][];

            for (int i = 0; i < totalFrames; i++)
            {
                int col = i % columns;
                int row = i / columns;
                int srcX = col * frameWidth;
                int srcY = row * frameHeight;

                var pixels = new byte[frameWidth * frameHeight * 4];

                for (int y = 0; y < frameHeight; y++)
                {
                    var srcOffset = atlasData.Scan0 + (srcY + y) * atlasStride + srcX * 4;
                    Marshal.Copy(srcOffset, pixels, y * frameWidth * 4, frameWidth * 4);
                }

                PreMultiplyAlpha(pixels);
                frames[i] = pixels;
            }

            return new SpriteSheet(frames, frameWidth, frameHeight);
        }
        finally
        {
            atlas.UnlockBits(atlasData);
        }
    }

    public ReadOnlySpan<byte> GetFrame(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)index >= (uint)_frames.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _frames[index];
    }

    private static void PreMultiplyAlpha(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte a = pixels[i + 3];
            if (a == 255) continue;
            if (a == 0) { pixels[i] = 0; pixels[i + 1] = 0; pixels[i + 2] = 0; continue; }
            pixels[i]     = (byte)(pixels[i]     * a / 255);
            pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
            pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
