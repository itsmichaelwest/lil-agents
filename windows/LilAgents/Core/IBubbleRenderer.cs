namespace LilAgents.Core;

/// <summary>
/// Renders a thinking/completion speech bubble above a character.
/// </summary>
public interface IBubbleRenderer : IDisposable
{
    void Show(string text, bool isCompletion, double x, double y, Windows.UI.Color borderColor, Windows.UI.Color textColor);
    void Hide();
    bool IsVisible { get; }
}
