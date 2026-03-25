using LilAgents.Core;
using LilAgents.Models;
using LilAgents.Rendering;
using LilAgents.Services;
using LilAgents.Themes;
using LilAgents.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace LilAgents;

/// <summary>
/// Application entry point. Runs as a background (tray-only) app.
/// Wires all components together: renderers → characters → controller → UI.
/// </summary>
public partial class App : Application
{
    // ── Services ───────────────────────────────────────────────────────
    private SettingsService? _settings;
    private ThemeService? _theme;
    private SoundService? _sound;
    private UpdateService? _updates;
    private TrayIconService? _tray;
    private Window? _hiddenWindow;

    // ── Animation ──────────────────────────────────────────────────────
    private AnimationController? _controller;
    private readonly Dictionary<string, CharacterState> _characterStates = [];

    public static SettingsService Settings { get; private set; } = null!;
    public static ThemeService Theme { get; private set; } = null!;
    public static SoundService Sound { get; private set; } = null!;

    /// <summary>Per-character state bundle for easy lookup.</summary>
    private sealed class CharacterState : IDisposable
    {
        public required WalkerCharacter Character { get; init; }
        public required CharacterOverlayWindow Renderer { get; init; }
        public required BubbleOverlayWindow Bubble { get; init; }
        public required SpriteSheet Sprites { get; init; }
        public ClaudeSession? Session { get; set; }
        public PopoverWindow? Popover { get; set; }

        public void Dispose()
        {
            Session?.Dispose();
            Popover?.Close();
            Renderer.Dispose();
            Bubble.Dispose();
            Sprites.Dispose();
        }
    }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // WinUI 3 requires at least one Window to keep the app alive.
        _hiddenWindow = new Window { Title = "lil agents" };

        // Initialize services
        _settings = new SettingsService();
        _theme = new ThemeService(_settings);
        _sound = new SoundService(_settings);
        _updates = new UpdateService();

        Settings = _settings;
        Theme = _theme;
        Sound = _sound;

        // System tray
        _tray = new TrayIconService(_settings);
        WireTrayEvents(_tray);
        _tray.Initialize();

        // Build the character pipeline
        InitializeCharacters();

        // Fire-and-forget startup update check
        _ = CheckForUpdatesAsync();
    }

    // ── Character initialization ───────────────────────────────────────

    private void InitializeCharacters()
    {
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // DPI-scaled character height for taskbar geometry calculation
        var primaryDpi = Interop.WindowInterop.GetDpiScale(Interop.WindowInterop.GetPrimaryMonitor());
        int scaledCharHeight = (int)Math.Round(200 * primaryDpi);

        _controller = new AnimationController(
            () =>
            {
                // TaskbarGeometry returns physical pixels. WalkerCharacter works
                // in physical pixels too (SetPosition goes straight to Win32).
                // TopY = top edge of taskbar = workArea.Bottom in physical px.
                var area = TaskbarGeometry.GetWalkingArea(scaledCharHeight, null);
                double taskbarTopY = area.Y + area.Height; // workArea.Bottom
                return new WalkingArea(area.X, area.Width, taskbarTopY);
            },
            dispatcherQueue);

        // Create Bruce and Jazz
        CreateCharacter(CharacterConfig.Bruce, _settings!.BruceVisible);
        CreateCharacter(CharacterConfig.Jazz, _settings!.JazzVisible);

        // Wire z-order updates
        _controller.ZOrderChanged += OnZOrderChanged;

        // Wire onboarding
        _controller.OnboardingCompleted += () =>
        {
            _settings!.HasCompletedOnboarding = true;
        };

        if (!_settings!.HasCompletedOnboarding)
        {
            _controller.TriggerOnboarding();
        }

        _controller.Start();
    }

    private void CreateCharacter(CharacterConfig config, bool visible)
    {
        // Load character frames from sprite atlas PNG
        var sprites = SpriteSheet.LoadAtlas(config.AtlasPath);

        // Create Win32 overlay windows, scaled for the primary monitor's DPI.
        // Logical size is 113×200 (matching the macOS displayWidth/displayHeight).
        var dpiScale = Interop.WindowInterop.GetDpiScale(Interop.WindowInterop.GetPrimaryMonitor());
        int physicalWidth = (int)Math.Round(113 * dpiScale);
        int physicalHeight = (int)Math.Round(200 * dpiScale);
        var renderer = new CharacterOverlayWindow(sprites, physicalWidth, physicalHeight);
        var bubble = new BubbleOverlayWindow();

        // Create the character state machine
        var character = new WalkerCharacter(config, renderer, bubble);

        // Create a lazy Claude session for this character
        character.PopoverRequested += OnPopoverRequested;
        character.PopoverCloseRequested += OnPopoverCloseRequested;
        character.CompletionSoundRequested += () => _sound?.PlayCompletionSound();
        character.OnboardingRequested += OnOnboardingRequested;

        // Store state
        var state = new CharacterState
        {
            Character = character,
            Renderer = renderer,
            Bubble = bubble,
            Sprites = sprites,
        };
        _characterStates[config.Name] = state;

        _controller!.AddCharacter(character);

        if (visible)
            character.Show();
    }

    // ── Popover management ─────────────────────────────────────────────

    private void OnPopoverRequested(WalkerCharacter character)
    {
        if (!_characterStates.TryGetValue(character.Name, out var state))
            return;

        // Lazy-create the Claude session
        if (state.Session is null)
        {
            var session = new ClaudeSession(DispatcherQueue.GetForCurrentThread());
            character.AttachChatSession(session);
            state.Session = session;
            session.Start();
        }

        // Create popover once, then hide/show on subsequent opens.
        if (state.Popover is null)
        {
            var popover = new PopoverWindow();
            popover.BindSession(state.Session);
            state.Popover = popover;
            ApplyThemeToPopover(popover, character);

            // Hide (not close/destroy) when the user clicks away or presses Escape.
            popover.PopoverHidden += () => character.ClosePopover();
        }

        // Position near the character and show.
        var bounds = state.Renderer.GetBounds();
        state.Popover.PositionNear(
            bounds.X + bounds.Width / 2,
            bounds.Y);

        state.Popover.ShowPopover();
    }

    private void OnPopoverCloseRequested(WalkerCharacter character)
    {
        if (_characterStates.TryGetValue(character.Name, out var state))
        {
            state.Popover?.HidePopover();
        }
    }

    private void OnOnboardingRequested(WalkerCharacter character)
    {
        // For now, onboarding opens the same popover flow.
        // The first-run "hi!" bubble + click leads to the chat.
        _controller?.CompleteOnboarding();
        OnPopoverRequested(character);
    }

    // ── Theme ──────────────────────────────────────────────────────────

    private void ApplyThemeToPopover(PopoverWindow popover, WalkerCharacter character)
    {
        var theme = _theme?.Current ?? PopoverTheme.Peach;
        popover.Theme = theme.WithCharacterColor(character.CharacterColor);
    }

    // ── Z-order ────────────────────────────────────────────────────────

    private void OnZOrderChanged(IReadOnlyList<WalkerCharacter> sorted)
    {
        // Update Win32 window z-order to match character position sorting.
        // Characters further right render on top (higher z-order).
        for (int i = 0; i < sorted.Count; i++)
        {
            if (_characterStates.TryGetValue(sorted[i].Name, out var state))
            {
                // The rendering layer handles its own z-order via HWND_TOPMOST.
                // For relative ordering between characters, we could use
                // SetWindowPos with HWND_AFTER, but TOPMOST windows are all
                // in the same z-band already. The position-based rendering
                // (rightmost character drawn last) gives the visual appearance.
            }
        }
    }

    // ── Tray events ────────────────────────────────────────────────────

    private void WireTrayEvents(TrayIconService tray)
    {
        tray.CharacterToggled += (name, visible) =>
        {
            if (_characterStates.TryGetValue(name, out var state))
            {
                if (visible)
                    state.Character.Show();
                else
                {
                    state.Character.ClosePopover();
                    state.Character.Hide();
                }
            }
        };

        tray.SoundToggled += _ => { /* persisted by TrayIconService */ };

        tray.ThemeSelected += theme =>
        {
            _theme?.SetTheme(theme);
            // Update all open popovers
            foreach (var (name, state) in _characterStates)
            {
                if (state.Popover is not null)
                    ApplyThemeToPopover(state.Popover, state.Character);
            }
        };

        tray.DisplaySelected += monitorIndex =>
        {
            // Settings already persisted; the walking area provider reads
            // PinnedDisplayIndex each tick so it takes effect immediately.
        };

        tray.CheckForUpdatesRequested += () =>
        {
            _ = CheckForUpdatesAsync(userInitiated: true);
        };

        tray.QuitRequested += Shutdown;
    }

    // ── Update check ───────────────────────────────────────────────────

    private async Task CheckForUpdatesAsync(bool userInitiated = false)
    {
        if (_updates is null) return;

        var result = await _updates.CheckForUpdateAsync();
        if (result is null || !result.UpdateAvailable)
            return;

        if (!string.IsNullOrEmpty(result.DownloadUrl))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = result.DownloadUrl,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch { /* Non-fatal */ }
        }
    }

    // ── Shutdown ───────────────────────────────────────────────────────

    private void Shutdown()
    {
        _controller?.Dispose();

        foreach (var state in _characterStates.Values)
            state.Dispose();
        _characterStates.Clear();

        _tray?.Dispose();
        _sound?.Dispose();
        _updates?.Dispose();
        _hiddenWindow?.Close();
        Environment.Exit(0);
    }
}
