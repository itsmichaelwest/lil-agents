using LilAgents.Core;
using LilAgents.Themes;

namespace LilAgents.Services;

/// <summary>
/// Manages the current theme, persists selection, and notifies listeners on change.
/// </summary>
public sealed class ThemeService : IThemeProvider
{
    private readonly SettingsService _settings;
    private PopoverTheme _current;

    public ThemeService(SettingsService settings)
    {
        _settings = settings;
        _current = ResolveTheme(_settings.CurrentThemeName);
    }

    public PopoverTheme Current => _current;

    public event Action<PopoverTheme>? ThemeChanged;

    public void SetTheme(PopoverTheme theme)
    {
        if (_current.Name == theme.Name)
            return;

        _current = theme;
        _settings.CurrentThemeName = theme.Name;
        ThemeChanged?.Invoke(theme);
    }

    /// <summary>
    /// Resolves a theme by name from the built-in presets.
    /// Falls back to Peach if the name is unrecognized.
    /// </summary>
    private static PopoverTheme ResolveTheme(string name)
    {
        foreach (var theme in PopoverTheme.All)
        {
            if (string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase))
                return theme;
        }
        return PopoverTheme.Peach;
    }
}
