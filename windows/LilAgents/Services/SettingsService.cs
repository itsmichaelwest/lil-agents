using System.Text.Json;

namespace LilAgents.Services;

/// <summary>
/// Persistent settings backed by a JSON file in the app's local data directory.
/// ApplicationData.Current.LocalSettings requires package identity, which
/// unpackaged apps don't have — so we use a plain file instead.
/// </summary>
public sealed class SettingsService
{
    private readonly string _filePath;
    private Dictionary<string, JsonElement> _data;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "LilAgents");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
        _data = Load();
    }

    public bool HasCompletedOnboarding
    {
        get => GetValue(nameof(HasCompletedOnboarding), false);
        set => SetValue(nameof(HasCompletedOnboarding), value);
    }

    public bool SoundsEnabled
    {
        get => GetValue(nameof(SoundsEnabled), true);
        set => SetValue(nameof(SoundsEnabled), value);
    }

    public string CurrentThemeName
    {
        get => GetValue(nameof(CurrentThemeName), "Peach");
        set => SetValue(nameof(CurrentThemeName), value);
    }

    public bool BruceVisible
    {
        get => GetValue(nameof(BruceVisible), true);
        set => SetValue(nameof(BruceVisible), value);
    }

    public bool JazzVisible
    {
        get => GetValue(nameof(JazzVisible), true);
        set => SetValue(nameof(JazzVisible), value);
    }

    public int PinnedDisplayIndex
    {
        get => GetValue(nameof(PinnedDisplayIndex), -1);
        set => SetValue(nameof(PinnedDisplayIndex), value);
    }

    public string SelectedProvider
    {
        get => GetValue(nameof(SelectedProvider), "Claude");
        set => SetValue(nameof(SelectedProvider), value);
    }

    private T GetValue<T>(string key, T defaultValue)
    {
        if (!_data.TryGetValue(key, out var element))
            return defaultValue;

        try
        {
            return element.Deserialize<T>() ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    private void SetValue<T>(string key, T value)
    {
        _data[key] = JsonSerializer.SerializeToElement(value);
        Save();
    }

    private Dictionary<string, JsonElement> Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                       ?? [];
            }
        }
        catch { /* Corrupted file — start fresh */ }
        return [];
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch { /* Non-fatal — settings will be lost on next restart */ }
    }
}
