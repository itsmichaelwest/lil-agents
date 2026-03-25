using Windows.UI;

namespace LilAgents.Models;

/// <summary>
/// Per-character configuration mirroring the Swift WalkerCharacter initialization.
/// </summary>
public sealed record CharacterConfig
{
    public required string Name { get; init; }
    public required string AtlasPath { get; init; }

    // Walking timing (seconds into animation loop)
    public required double AccelStart { get; init; }
    public required double FullSpeedStart { get; init; }
    public required double DecelStart { get; init; }
    public required double WalkStop { get; init; }

    // Walk distance as fraction of dock width
    public required double WalkAmountMin { get; init; }
    public required double WalkAmountMax { get; init; }

    public double YOffset { get; init; }
    public double FlipXOffset { get; init; }
    public double InitialPositionProgress { get; init; }
    public double InitialPauseMin { get; init; }
    public double InitialPauseMax { get; init; }

    public required Color CharacterColor { get; init; }

    /// <summary>Bruce configuration matching Swift source.</summary>
    public static CharacterConfig Bruce => new()
    {
        Name = "Bruce",
        AtlasPath = "Assets/Sprites/walk-bruce-01.png",
        AccelStart = 3.0,
        FullSpeedStart = 3.75,
        DecelStart = 8.0,
        WalkStop = 8.5,
        WalkAmountMin = 0.4,
        WalkAmountMax = 0.65,
        YOffset = -3,
        FlipXOffset = 0,
        InitialPositionProgress = 0.3,
        InitialPauseMin = 0.5,
        InitialPauseMax = 2.0,
        CharacterColor = Color.FromArgb(255, 102, 184, 140) // (0.4, 0.72, 0.55)
    };

    /// <summary>Jazz configuration matching Swift source.</summary>
    public static CharacterConfig Jazz => new()
    {
        Name = "Jazz",
        AtlasPath = "Assets/Sprites/walk-jazz-01.png",
        AccelStart = 3.9,
        FullSpeedStart = 4.5,
        DecelStart = 8.0,
        WalkStop = 8.75,
        WalkAmountMin = 0.35,
        WalkAmountMax = 0.6,
        YOffset = -7,
        FlipXOffset = -9,
        InitialPositionProgress = 0.7,
        InitialPauseMin = 8.0,
        InitialPauseMax = 14.0,
        CharacterColor = Color.FromArgb(255, 255, 102, 0) // (1.0, 0.4, 0.0)
    };
}
