using System.Diagnostics;
using LilAgents.Models;
using LilAgents.Themes;
using Windows.UI;

namespace LilAgents.Core;

/// <summary>
/// Walking area geometry provided by the animation controller each tick.
/// On Windows this maps to the taskbar region; on macOS it was the dock area.
/// </summary>
public readonly record struct WalkingArea(double X, double Width, double TopY);

/// <summary>
/// Character state machine: walking physics, pause/resume, thinking bubbles,
/// popover idle coordination, and frame selection.
/// Direct port of WalkerCharacter.swift with identical physics.
/// </summary>
public sealed class WalkerCharacter
{
    // ── Configuration (immutable after construction) ──────────────────

    public CharacterConfig Config { get; }
    public string Name => Config.Name;
    public Color CharacterColor => Config.CharacterColor;

    private readonly double _accelStart;
    private readonly double _fullSpeedStart;
    private readonly double _decelStart;
    private readonly double _walkStop;
    private readonly double _walkAmountMin;
    private readonly double _walkAmountMax;
    private readonly double _yOffset;
    private readonly double _flipXOffset;

    // ── Sprite dimensions ────────────────────────────────────────────

    // Video properties — must match the actual .mov files.
    // 24fps, 241 frames, ~10.04s (verified via ffprobe).
    private const double VideoDuration = 10.041667;
    private const double FrameRate = 24.0;

    // Actual display size in physical pixels, derived from the renderer.
    // These replace the macOS hardcoded 200/113 — they account for DPI.
    private readonly double _displayHeight;
    private readonly double _displayWidth;

    // ── Injected dependencies ────────────────────────────────────────

    private readonly ICharacterRenderer _renderer;
    private readonly IBubbleRenderer _bubble;
    private IChatSession? _chatSession;

    // ── Walk state ───────────────────────────────────────────────────

    private double _walkStartTime;
    public double PositionProgress { get; internal set; }
    public bool IsWalking { get; private set; }
    public bool IsPaused { get; private set; } = true;
    internal double PauseEndTime { get; set; }
    public bool GoingRight { get; private set; } = true;
    private double _walkStartPos;
    private double _walkEndPos;
    private double _currentTravelDistance = 500.0;
    private double _walkStartPixel;
    private double _walkEndPixel;

    // ── Popover / idle state ─────────────────────────────────────────

    public bool IsIdleForPopover { get; private set; }
    public bool IsOnboarding { get; internal set; }

    // ── Thinking bubble state ────────────────────────────────────────

    private static readonly string[] ThinkingPhrases =
    [
        "hmm...", "thinking...", "one sec...", "ok hold on",
        "let me check", "working on it", "almost...", "bear with me",
        "on it!", "gimme a sec", "brb", "processing...",
        "hang tight", "just a moment", "figuring it out",
        "crunching...", "reading...", "looking..."
    ];

    private static readonly string[] CompletionPhrases =
    [
        "done!", "all set!", "ready!", "here you go", "got it!",
        "finished!", "ta-da!", "voila!"
    ];

    private double _lastPhraseUpdate;
    private string _currentPhrase = "";
    private double _completionBubbleExpiry;
    internal bool ShowingCompletion { get; set; }
    private double _nextPhraseInterval;

    // ── Siblings reference (set by AnimationController) ──────────────

    internal List<WalkerCharacter>? Siblings { get; set; }

    // ── Time source (monotonic seconds) ──────────────────────────────

    private static readonly long StartTimestamp = Stopwatch.GetTimestamp();

    internal static double Now()
    {
        return Stopwatch.GetElapsedTime(StartTimestamp).TotalSeconds;
    }

    // ── Events ───────────────────────────────────────────────────────

    /// <summary>Raised when a completion sound should play.</summary>
    public event Action? CompletionSoundRequested;

    /// <summary>Raised when the popover should open.</summary>
    public event Action<WalkerCharacter>? PopoverRequested;

    /// <summary>Raised when the popover should close.</summary>
    public event Action<WalkerCharacter>? PopoverCloseRequested;

    /// <summary>Raised when onboarding popover should open.</summary>
    public event Action<WalkerCharacter>? OnboardingRequested;

    // ── Constructor ──────────────────────────────────────────────────

    public WalkerCharacter(
        CharacterConfig config,
        ICharacterRenderer renderer,
        IBubbleRenderer bubble)
    {
        Config = config;
        _renderer = renderer;
        _bubble = bubble;

        // Get actual physical pixel size from the renderer (DPI-scaled).
        var bounds = renderer.GetBounds();
        _displayWidth = bounds.Width > 0 ? bounds.Width : 113;
        _displayHeight = bounds.Height > 0 ? bounds.Height : 200;

        _accelStart = config.AccelStart;
        _fullSpeedStart = config.FullSpeedStart;
        _decelStart = config.DecelStart;
        _walkStop = config.WalkStop;
        _walkAmountMin = config.WalkAmountMin;
        _walkAmountMax = config.WalkAmountMax;
        _yOffset = config.YOffset;
        _flipXOffset = config.FlipXOffset;

        PositionProgress = config.InitialPositionProgress;

        double pauseDelay = config.InitialPauseMin +
            Random.Shared.NextDouble() * (config.InitialPauseMax - config.InitialPauseMin);
        PauseEndTime = Now() + pauseDelay;

        _nextPhraseInterval = RandomPhraseInterval();

        _renderer.Clicked += OnRendererClicked;
    }

    // ── Chat session wiring ──────────────────────────────────────────

    public void AttachChatSession(IChatSession session)
    {
        DetachChatSession();
        _chatSession = session;
        session.TurnCompleted += OnTurnCompleted;
    }

    public void DetachChatSession()
    {
        if (_chatSession is { } old)
        {
            old.TurnCompleted -= OnTurnCompleted;
            _chatSession = null;
        }
    }

    private bool IsClaudeBusy => _chatSession?.IsBusy ?? false;

    private void OnTurnCompleted()
    {
        CompletionSoundRequested?.Invoke();
        ShowCompletionBubble();
    }

    // ── Click handling ───────────────────────────────────────────────

    private void OnRendererClicked(double x, double y)
    {
        if (IsOnboarding)
        {
            OnboardingRequested?.Invoke(this);
            return;
        }

        if (IsIdleForPopover)
        {
            ClosePopover();
        }
        else
        {
            OpenPopover();
        }
    }

    public void HandleClick()
    {
        OnRendererClicked(0, 0);
    }

    // ── Popover coordination ─────────────────────────────────────────

    public void OpenPopover()
    {
        // Close any sibling popovers first
        if (Siblings is { } siblings)
        {
            foreach (var sibling in siblings)
            {
                if (sibling != this && sibling.IsIdleForPopover)
                    sibling.ClosePopover();
            }
        }

        IsIdleForPopover = true;
        IsWalking = false;
        IsPaused = true;

        ShowingCompletion = false;
        HideBubble();

        SetFrame(0, !GoingRight);

        PopoverRequested?.Invoke(this);
    }

    public void ClosePopover()
    {
        if (!IsIdleForPopover) return;

        IsIdleForPopover = false;

        // If still waiting for a response, show thinking bubble immediately
        // If completion came while popover was open, show completion bubble
        if (ShowingCompletion)
        {
            _completionBubbleExpiry = Now() + 3.0;
            ShowBubble(_currentPhrase, isCompletion: true);
        }
        else if (IsClaudeBusy)
        {
            _currentPhrase = "";
            _lastPhraseUpdate = 0;
            UpdateThinkingPhrase();
            ShowBubble(_currentPhrase, isCompletion: false);
        }

        double delay = 2.0 + Random.Shared.NextDouble() * 3.0;
        PauseEndTime = Now() + delay;

        PopoverCloseRequested?.Invoke(this);
    }

    // ── Onboarding ───────────────────────────────────────────────────

    public void EnterOnboardingIdle()
    {
        ShowingCompletion = false;
        HideBubble();
        IsIdleForPopover = true;
        IsWalking = false;
        IsPaused = true;
        SetFrame(0, false);
    }

    public void ExitOnboarding()
    {
        IsIdleForPopover = false;
        IsOnboarding = false;
        IsPaused = true;
        PauseEndTime = Now() + 1.0 + Random.Shared.NextDouble() * 2.0;
    }

    public void ShowOnboardingBubble()
    {
        _currentPhrase = "hi!";
        ShowingCompletion = true;
        _completionBubbleExpiry = Now() + 600; // stays until clicked
    }

    // ── Walking ──────────────────────────────────────────────────────

    public void StartWalk()
    {
        IsPaused = false;
        IsWalking = true;
        _walkStartTime = Now();

        // Direction bias near edges, random in the middle
        if (PositionProgress > 0.85)
            GoingRight = false;
        else if (PositionProgress < 0.15)
            GoingRight = true;
        else
            GoingRight = Random.Shared.Next(2) == 0;

        _walkStartPos = PositionProgress;

        // Walk a fixed pixel distance regardless of screen width
        const double referenceWidth = 500.0;
        double walkFraction = _walkAmountMin +
            Random.Shared.NextDouble() * (_walkAmountMax - _walkAmountMin);
        double walkPixels = walkFraction * referenceWidth;
        double walkAmount = _currentTravelDistance > 0
            ? walkPixels / _currentTravelDistance
            : 0.3;

        if (GoingRight)
            _walkEndPos = Math.Min(_walkStartPos + walkAmount, 1.0);
        else
            _walkEndPos = Math.Max(_walkStartPos - walkAmount, 0.0);

        // Store pixel positions for consistent speed across screen changes
        _walkStartPixel = _walkStartPos * _currentTravelDistance;
        _walkEndPixel = _walkEndPos * _currentTravelDistance;

        // Avoid collisions with siblings
        AvoidSiblingCollision();

        UpdateFrame();
    }

    private void AvoidSiblingCollision()
    {
        const double minSeparation = 0.12;
        if (Siblings is not { } siblings) return;

        foreach (var sibling in siblings)
        {
            if (sibling == this) continue;
            double sibPos = sibling.PositionProgress;
            if (Math.Abs(_walkEndPos - sibPos) < minSeparation)
            {
                if (GoingRight)
                    _walkEndPos = Math.Max(_walkStartPos, sibPos - minSeparation);
                else
                    _walkEndPos = Math.Min(_walkStartPos, sibPos + minSeparation);
            }
        }
    }

    public void EnterPause()
    {
        IsWalking = false;
        IsPaused = true;
        double delay = 5.0 + Random.Shared.NextDouble() * 7.0;
        PauseEndTime = Now() + delay;
        SetFrame(0, !GoingRight);
    }

    // ── Walking physics (exact port of movementPosition) ─────────────

    /// <summary>
    /// Maps video time to normalized walk progress (0.0 to 1.0) using
    /// acceleration / linear / deceleration easing.
    /// Exact port of WalkerCharacter.swift movementPosition(at:).
    /// </summary>
    public double MovementPosition(double videoTime)
    {
        double dIn = _fullSpeedStart - _accelStart;
        double dLin = _decelStart - _fullSpeedStart;
        double dOut = _walkStop - _decelStart;
        double v = 1.0 / (dIn / 2.0 + dLin + dOut / 2.0);

        if (videoTime <= _accelStart)
        {
            return 0.0;
        }
        else if (videoTime <= _fullSpeedStart)
        {
            double t = videoTime - _accelStart;
            return v * t * t / (2.0 * dIn);
        }
        else if (videoTime <= _decelStart)
        {
            double easeInDist = v * dIn / 2.0;
            double t = videoTime - _fullSpeedStart;
            return easeInDist + v * t;
        }
        else if (videoTime <= _walkStop)
        {
            double easeInDist = v * dIn / 2.0;
            double linearDist = v * dLin;
            double t = videoTime - _decelStart;
            return easeInDist + linearDist + v * (t - t * t / (2.0 * dOut));
        }
        else
        {
            return 1.0;
        }
    }

    // ── Frame selection ──────────────────────────────────────────────

    private double CurrentFlipCompensation => GoingRight ? 0 : _flipXOffset;

    private void SetFrame(int frameIndex, bool flipped)
    {
        _renderer.SetFrame(frameIndex, flipped);
    }

    private void UpdateFrame()
    {
        if (!IsWalking) return;
        double elapsed = Now() - _walkStartTime;
        double videoTime = Math.Min(elapsed, VideoDuration);
        int frame = Math.Clamp((int)(videoTime * FrameRate), 0, _renderer.FrameCount - 1);
        SetFrame(frame, !GoingRight);
    }

    // ── Frame update (called by AnimationController each tick) ───────

    public void Update(in WalkingArea area)
    {
        _currentTravelDistance = Math.Max(area.Width - _displayWidth, 0);

        if (IsIdleForPopover)
        {
            UpdateIdlePosition(area);
            UpdateThinkingBubble();
            return;
        }

        double now = Now();

        if (IsPaused)
        {
            if (now >= PauseEndTime)
            {
                StartWalk();
            }
            else
            {
                UpdatePausedPosition(area);
                UpdateThinkingBubble();
                return;
            }
        }

        if (IsWalking)
        {
            double elapsed = now - _walkStartTime;
            double videoTime = Math.Min(elapsed, VideoDuration);

            // Interpolate in pixel space for consistent speed across screen changes
            double walkNorm = elapsed >= VideoDuration
                ? 1.0
                : MovementPosition(videoTime);
            double currentPixel = _walkStartPixel +
                (_walkEndPixel - _walkStartPixel) * walkNorm;

            if (_currentTravelDistance > 0)
            {
                PositionProgress = Math.Clamp(
                    currentPixel / _currentTravelDistance, 0.0, 1.0);
            }

            if (elapsed >= VideoDuration)
            {
                _walkEndPos = PositionProgress;
                EnterPause();
                UpdatePausedPosition(area);
                UpdateThinkingBubble();
                return;
            }

            double x = area.X + _currentTravelDistance * PositionProgress
                + CurrentFlipCompensation;
            // bottomPadding compensates for transparent pixels at bottom of sprite.
            // Added (not subtracted) because Windows Y goes down, opposite of macOS.
            double bottomPadding = _displayHeight * 0.19;
            double y = area.TopY - _displayHeight + bottomPadding + _yOffset;
            _renderer.SetPosition(x, y);

            UpdateFrame();
        }

        UpdateThinkingBubble();
    }

    private void UpdateIdlePosition(in WalkingArea area)
    {
        double x = area.X + _currentTravelDistance * PositionProgress
            + CurrentFlipCompensation;
        double bottomPadding = _displayHeight * 0.19;
        double y = area.TopY - _displayHeight + bottomPadding + _yOffset;
        _renderer.SetPosition(x, y);
    }

    private void UpdatePausedPosition(in WalkingArea area)
    {
        double travelDistance = Math.Max(area.Width - _displayWidth, 0);
        double x = area.X + travelDistance * PositionProgress
            + CurrentFlipCompensation;
        double bottomPadding = _displayHeight * 0.19;
        double y = area.TopY - _displayHeight + bottomPadding + _yOffset;
        _renderer.SetPosition(x, y);
    }

    // ── Thinking bubble ──────────────────────────────────────────────

    private void UpdateThinkingBubble()
    {
        double now = Now();

        if (ShowingCompletion)
        {
            if (now >= _completionBubbleExpiry)
            {
                ShowingCompletion = false;
                HideBubble();
                return;
            }
            if (IsIdleForPopover)
            {
                // Freeze expiry while popover is open
                _completionBubbleExpiry += 1.0 / 60.0;
                HideBubble();
            }
            else
            {
                ShowBubble(_currentPhrase, isCompletion: true);
            }
            return;
        }

        if (IsClaudeBusy && !IsIdleForPopover)
        {
            string oldPhrase = _currentPhrase;
            UpdateThinkingPhrase();
            // Always show the bubble; phrase changes will update text
            ShowBubble(_currentPhrase, isCompletion: false);
        }
        else if (!ShowingCompletion)
        {
            HideBubble();
        }
    }

    private void UpdateThinkingPhrase()
    {
        double now = Now();
        if (string.IsNullOrEmpty(_currentPhrase) ||
            now - _lastPhraseUpdate > _nextPhraseInterval)
        {
            string next = ThinkingPhrases[Random.Shared.Next(ThinkingPhrases.Length)];
            // Avoid repeating the same phrase
            while (next == _currentPhrase && ThinkingPhrases.Length > 1)
                next = ThinkingPhrases[Random.Shared.Next(ThinkingPhrases.Length)];

            _currentPhrase = next;
            _lastPhraseUpdate = now;
            _nextPhraseInterval = RandomPhraseInterval();
        }
    }

    private void ShowCompletionBubble()
    {
        _currentPhrase = CompletionPhrases[
            Random.Shared.Next(CompletionPhrases.Length)];
        ShowingCompletion = true;
        _completionBubbleExpiry = Now() + 3.0;
        _lastPhraseUpdate = 0;

        if (!IsIdleForPopover)
            ShowBubble(_currentPhrase, isCompletion: true);
    }

    private void ShowBubble(string text, bool isCompletion)
    {
        if (string.IsNullOrEmpty(text)) return;

        var bounds = _renderer.GetBounds();
        double x = bounds.X + bounds.Width / 2;
        // macOS (Y-up): y = origin.y + height * 0.88 puts bubble near head.
        // Windows (Y-down): equivalent is top + height * 0.12.
        double y = bounds.Y + bounds.Height * 0.12;

        // Resolve bubble colors from theme defaults
        // The bubble renderer handles theme-specific styling;
        // we pass the character color and completion state.
        Color borderColor = isCompletion
            ? Color.FromArgb(179, 77, 191, 128)
            : CharacterColor;
        Color textColor = isCompletion
            ? Color.FromArgb(255, 51, 153, 102)
            : Color.FromArgb(255, 140, 128, 133);

        _bubble.Show(text, isCompletion, x, y, borderColor, textColor);
    }

    private void HideBubble()
    {
        if (_bubble.IsVisible)
            _bubble.Hide();
    }

    private static double RandomPhraseInterval()
    {
        return 3.0 + Random.Shared.NextDouble() * 2.0;
    }

    // ── Visibility ───────────────────────────────────────────────────

    public bool IsVisible => _renderer.IsVisible;

    public void Show() => _renderer.Show();
    public void Hide()
    {
        _renderer.Hide();
        HideBubble();
    }
}
