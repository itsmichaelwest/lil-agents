using Windows.Media.Core;
using Windows.Media.Playback;

namespace LilAgents.Services;

/// <summary>
/// Plays a random completion ping sound from the Assets/Sounds directory.
/// Uses Windows.Media.Playback.MediaPlayer for audio output.
/// </summary>
public sealed class SoundService : IDisposable
{
    private static readonly string[] SoundFiles =
    [
        "ping-aa.mp3",
        "ping-bb.mp3",
        "ping-cc.mp3",
        "ping-dd.mp3",
        "ping-ee.mp3",
        "ping-ff.mp3",
        "ping-gg.mp3",
        "ping-hh.mp3",
        "ping-jj.m4a"
    ];

    private readonly SettingsService _settings;
    private readonly Random _random = new();
    private readonly string _soundsDir;
    private MediaPlayer? _player;
    private bool _disposed;

    public SoundService(SettingsService settings)
    {
        _settings = settings;
        _soundsDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds");
    }

    /// <summary>
    /// Plays a random completion ping if sounds are enabled.
    /// </summary>
    public void PlayCompletionSound()
    {
        if (_disposed || !_settings.SoundsEnabled)
            return;

        var fileName = SoundFiles[_random.Next(SoundFiles.Length)];
        var filePath = Path.Combine(_soundsDir, fileName);

        if (!File.Exists(filePath))
            return;

        try
        {
            // Dispose previous player to avoid overlapping playback issues
            _player?.Dispose();
            _player = new MediaPlayer
            {
                Source = MediaSource.CreateFromUri(new Uri(filePath)),
                AudioCategory = MediaPlayerAudioCategory.SoundEffects
            };
            _player.Play();
        }
        catch
        {
            // Sound playback is best-effort; swallow failures.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _player?.Dispose();
        _player = null;
    }
}
