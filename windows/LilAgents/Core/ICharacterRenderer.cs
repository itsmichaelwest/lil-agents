using Windows.Foundation;

namespace LilAgents.Core;

/// <summary>
/// Renders a character sprite on a transparent overlay window above the taskbar.
/// Implemented by CharacterOverlayWindow using Win32 layered windows.
/// </summary>
public interface ICharacterRenderer : IDisposable
{
    void SetPosition(double x, double y);
    void SetFrame(int frameIndex, bool flipped);
    int FrameCount { get; }
    void Show();
    void Hide();
    bool IsVisible { get; }
    Rect GetBounds();

    /// <summary>Fires with screen coordinates when the character is clicked.</summary>
    event Action<double, double>? Clicked;
}
