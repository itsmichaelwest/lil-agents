using Microsoft.UI.Dispatching;

namespace LilAgents.Core;

/// <summary>
/// Delegate that provides the current walking area geometry each tick.
/// The implementation (taskbar geometry polling) lives outside this class.
/// </summary>
public delegate WalkingArea WalkingAreaProvider();

/// <summary>
/// 60fps tick loop coordinating all characters: walk stagger logic,
/// position updates, and z-order management.
/// Direct port of LilAgentsController.swift tick loop.
/// </summary>
public sealed class AnimationController : IDisposable
{
    private readonly List<WalkerCharacter> _characters = [];
    private readonly DispatcherQueueTimer _timer;
    private readonly WalkingAreaProvider _walkingAreaProvider;
    private bool _disposed;

    // ── Onboarding ───────────────────────────────────────────────────

    private bool _onboardingTriggered;
    private double _onboardingBubbleTime;

    /// <summary>
    /// Raised when onboarding completes so the app layer can persist the flag.
    /// </summary>
    public event Action? OnboardingCompleted;

    /// <summary>
    /// Raised each tick with characters sorted by position for z-ordering.
    /// The renderer layer uses this to update overlay window z-order.
    /// Index 0 = back (lowest), last = front (highest).
    /// </summary>
    public event Action<IReadOnlyList<WalkerCharacter>>? ZOrderChanged;

    // ── Construction ─────────────────────────────────────────────────

    /// <summary>
    /// Creates the animation controller.
    /// </summary>
    /// <param name="walkingAreaProvider">
    /// Called each tick to get the current walking area geometry.
    /// </param>
    /// <param name="dispatcherQueue">
    /// The UI thread dispatcher queue (from the main window).
    /// </param>
    public AnimationController(
        WalkingAreaProvider walkingAreaProvider,
        DispatcherQueue dispatcherQueue)
    {
        _walkingAreaProvider = walkingAreaProvider
            ?? throw new ArgumentNullException(nameof(walkingAreaProvider));

        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16); // ~60fps
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => Tick();
    }

    // ── Character management ─────────────────────────────────────────

    public IReadOnlyList<WalkerCharacter> Characters => _characters;

    public void AddCharacter(WalkerCharacter character)
    {
        _characters.Add(character);
        // Give every character a reference to all siblings
        foreach (var c in _characters)
            c.Siblings = _characters;
    }

    public void RemoveCharacter(WalkerCharacter character)
    {
        _characters.Remove(character);
        character.Siblings = null;
        foreach (var c in _characters)
            c.Siblings = _characters;
    }

    // ── Lifecycle ────────────────────────────────────────────────────

    public void Start()
    {
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    // ── Onboarding ───────────────────────────────────────────────────

    /// <summary>
    /// Call after adding characters if onboarding has not been completed.
    /// Triggers the "hi!" bubble on the first character after a 2-second delay.
    /// </summary>
    public void TriggerOnboarding()
    {
        if (_onboardingTriggered || _characters.Count == 0) return;
        _onboardingTriggered = true;

        var first = _characters[0];
        first.IsOnboarding = true;

        // Show bubble after 2 seconds so the character is visible first.
        // We store the target time and check in Tick().
        _onboardingBubbleTime = WalkerCharacter.Now() + 2.0;
    }

    public void CompleteOnboarding()
    {
        foreach (var c in _characters)
            c.IsOnboarding = false;
        OnboardingCompleted?.Invoke();
    }

    // ── Core tick loop ───────────────────────────────────────────────

    private void Tick()
    {
        // Deferred onboarding bubble
        if (_onboardingBubbleTime > 0 && WalkerCharacter.Now() >= _onboardingBubbleTime)
        {
            _onboardingBubbleTime = 0;
            if (_characters.Count > 0)
                _characters[0].ShowOnboardingBubble();
        }

        WalkingArea area = _walkingAreaProvider();

        var activeChars = GetVisibleCharacters();
        if (activeChars.Count == 0) return;

        // Stagger logic: if any character is walking, delay paused characters
        // that are ready to start walking. This prevents both characters from
        // walking at the same time (port of LilAgentsController.tick).
        ApplyWalkStagger(activeChars);

        // Update each character
        foreach (var c in activeChars)
        {
            c.Update(area);
        }

        // Z-order: characters closer to the right render on top
        SortAndNotifyZOrder(activeChars);
    }

    private List<WalkerCharacter> GetVisibleCharacters()
    {
        var result = new List<WalkerCharacter>(_characters.Count);
        foreach (var c in _characters)
        {
            if (c.IsVisible)
                result.Add(c);
        }
        return result;
    }

    private static void ApplyWalkStagger(List<WalkerCharacter> activeChars)
    {
        double now = WalkerCharacter.Now();
        bool anyWalking = false;
        foreach (var c in activeChars)
        {
            if (c.IsWalking)
            {
                anyWalking = true;
                break;
            }
        }

        if (!anyWalking) return;

        foreach (var c in activeChars)
        {
            if (c.IsIdleForPopover) continue;
            if (c.IsPaused && now >= c.PauseEndTime)
            {
                // Another character is already walking; push this one's pause out
                c.PauseEndTime = now + 5.0 + Random.Shared.NextDouble() * 5.0;
            }
        }
    }

    private void SortAndNotifyZOrder(List<WalkerCharacter> activeChars)
    {
        activeChars.Sort((a, b) =>
            a.PositionProgress.CompareTo(b.PositionProgress));
        ZOrderChanged?.Invoke(activeChars);
    }

    // ── IDisposable ──────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
    }
}
