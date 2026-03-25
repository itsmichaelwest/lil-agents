using LilAgents.Themes;

namespace LilAgents.Core;

/// <summary>
/// Provides the current theme and notifies when it changes.
/// </summary>
public interface IThemeProvider
{
    PopoverTheme Current { get; }
    event Action<PopoverTheme>? ThemeChanged;
    void SetTheme(PopoverTheme theme);
}
