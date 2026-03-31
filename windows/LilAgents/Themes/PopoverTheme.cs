using System.Diagnostics.CodeAnalysis;
using LilAgents.Core;
using Windows.UI;

namespace LilAgents.Themes;

/// <summary>
/// Theme definition for the chat popover and speech bubbles.
/// Port of PopoverTheme.swift with all 4 presets preserved exactly.
/// </summary>
public sealed record PopoverTheme
{
    /// <summary>
    /// Parameterless constructor required by the WinUI XAML type system.
    /// Defaults to the Peach theme values (hardcoded to avoid static init circularity).
    /// </summary>
    [SetsRequiredMembers]
    public PopoverTheme()
    {
        Name = "Peach";
        PopoverBg = Color.FromArgb(247, 255, 247, 235);
        PopoverBorder = Color.FromArgb(204, 242, 140, 166);
        PopoverBorderWidth = 2.5;
        PopoverCornerRadius = 24;
        TitleBarBg = Color.FromArgb(255, 250, 237, 224);
        TitleText = Color.FromArgb(255, 217, 89, 115);
        TitleFormat = TitleFormat.LowercaseTilde;
        TitleFontFamily = "Segoe UI Variable";
        TitleFontSize = 12;
        SeparatorColor = Color.FromArgb(64, 242, 140, 166);
        FontFamily = "Segoe UI Variable";
        FontSize = 12;
        FontBoldFamily = "Segoe UI Variable";
        TextPrimary = Color.FromArgb(255, 51, 46, 56);
        TextDim = Color.FromArgb(255, 128, 120, 133);
        AccentColor = Color.FromArgb(255, 217, 89, 115);
        ErrorColor = Color.FromArgb(255, 230, 77, 51);
        SuccessColor = Color.FromArgb(255, 77, 184, 128);
        InputBg = Color.FromArgb(255, 255, 250, 242);
        InputCornerRadius = 14;
        BubbleBg = Color.FromArgb(242, 255, 242, 230);
        BubbleBorder = Color.FromArgb(153, 242, 140, 166);
        BubbleText = Color.FromArgb(255, 140, 128, 133);
        BubbleCompletionBorder = Color.FromArgb(179, 77, 191, 128);
        BubbleCompletionText = Color.FromArgb(255, 51, 153, 102);
        BubbleFontFamily = "Segoe UI Variable";
        BubbleFontSize = 11;
        BubbleCornerRadius = 14;
    }

    public required string Name { get; init; }

    // Popover
    public required Color PopoverBg { get; init; }
    public required Color PopoverBorder { get; init; }
    public required double PopoverBorderWidth { get; init; }
    public required double PopoverCornerRadius { get; init; }
    public required Color TitleBarBg { get; init; }
    public required Color TitleText { get; init; }
    public required TitleFormat TitleFormat { get; init; }
    public string TitleString => AgentProviderExtensions.Current.TitleString(TitleFormat);
    public required string TitleFontFamily { get; init; }
    public required double TitleFontSize { get; init; }
    public required Color SeparatorColor { get; init; }

    // Terminal
    public required string FontFamily { get; init; }
    public required double FontSize { get; init; }
    public required string FontBoldFamily { get; init; }
    public required Color TextPrimary { get; init; }
    public required Color TextDim { get; init; }
    public required Color AccentColor { get; init; }
    public required Color ErrorColor { get; init; }
    public required Color SuccessColor { get; init; }
    public required Color InputBg { get; init; }
    public required double InputCornerRadius { get; init; }

    // Bubble
    public required Color BubbleBg { get; init; }
    public required Color BubbleBorder { get; init; }
    public required Color BubbleText { get; init; }
    public required Color BubbleCompletionBorder { get; init; }
    public required Color BubbleCompletionText { get; init; }
    public required string BubbleFontFamily { get; init; }
    public required double BubbleFontSize { get; init; }
    public required double BubbleCornerRadius { get; init; }

    // Presets — stub; Agent 3 fills in exact color values from PopoverTheme.swift
    public static PopoverTheme Peach => _peach;
    public static PopoverTheme Midnight => _midnight;
    public static PopoverTheme Cloud => _cloud;
    public static PopoverTheme Moss => _moss;
    public static PopoverTheme[] All => [Peach, Midnight, Cloud, Moss];

    private static readonly PopoverTheme _peach = new()
    {
        Name = "Peach",
        PopoverBg = Color.FromArgb(247, 255, 247, 235),
        PopoverBorder = Color.FromArgb(204, 242, 140, 166),
        PopoverBorderWidth = 2.5,
        PopoverCornerRadius = 24,
        TitleBarBg = Color.FromArgb(255, 250, 237, 224),
        TitleText = Color.FromArgb(255, 217, 89, 115),
        TitleFormat = TitleFormat.LowercaseTilde,
        TitleFontFamily = "Segoe UI Variable",
        TitleFontSize = 12,
        SeparatorColor = Color.FromArgb(64, 242, 140, 166),
        FontFamily = "Segoe UI Variable",
        FontSize = 12,
        FontBoldFamily = "Segoe UI Variable",
        TextPrimary = Color.FromArgb(255, 51, 46, 56),
        TextDim = Color.FromArgb(255, 128, 120, 133),
        AccentColor = Color.FromArgb(255, 217, 89, 115),
        ErrorColor = Color.FromArgb(255, 230, 77, 51),
        SuccessColor = Color.FromArgb(255, 77, 184, 128),
        InputBg = Color.FromArgb(255, 255, 250, 242),
        InputCornerRadius = 14,
        BubbleBg = Color.FromArgb(242, 255, 242, 230),
        BubbleBorder = Color.FromArgb(153, 242, 140, 166),
        BubbleText = Color.FromArgb(255, 140, 128, 133),
        BubbleCompletionBorder = Color.FromArgb(179, 77, 191, 128),
        BubbleCompletionText = Color.FromArgb(255, 51, 153, 102),
        BubbleFontFamily = "Segoe UI Variable",
        BubbleFontSize = 11,
        BubbleCornerRadius = 14
    };

    private static readonly PopoverTheme _midnight = new()
    {
        Name = "Midnight",
        PopoverBg = Color.FromArgb(245, 18, 18, 18),
        PopoverBorder = Color.FromArgb(179, 255, 102, 0),
        PopoverBorderWidth = 1.5,
        PopoverCornerRadius = 12,
        TitleBarBg = Color.FromArgb(255, 26, 26, 26),
        TitleText = Color.FromArgb(255, 255, 102, 0),
        TitleFormat = TitleFormat.Uppercase,
        TitleFontFamily = "Cascadia Mono",
        TitleFontSize = 10,
        SeparatorColor = Color.FromArgb(77, 255, 102, 0),
        FontFamily = "Cascadia Mono",
        FontSize = 11.5,
        FontBoldFamily = "Cascadia Mono",
        TextPrimary = Color.FromArgb(255, 255, 255, 255),
        TextDim = Color.FromArgb(255, 153, 153, 153),
        AccentColor = Color.FromArgb(255, 255, 102, 0),
        ErrorColor = Color.FromArgb(255, 255, 77, 51),
        SuccessColor = Color.FromArgb(255, 102, 166, 102),
        InputBg = Color.FromArgb(255, 31, 31, 31),
        InputCornerRadius = 4,
        BubbleBg = Color.FromArgb(235, 26, 26, 26),
        BubbleBorder = Color.FromArgb(153, 255, 102, 0),
        BubbleText = Color.FromArgb(255, 179, 179, 179),
        BubbleCompletionBorder = Color.FromArgb(179, 77, 204, 77),
        BubbleCompletionText = Color.FromArgb(255, 77, 217, 77),
        BubbleFontFamily = "Cascadia Mono",
        BubbleFontSize = 10,
        BubbleCornerRadius = 12
    };

    private static readonly PopoverTheme _cloud = new()
    {
        Name = "Cloud",
        PopoverBg = Color.FromArgb(250, 240, 242, 245),
        PopoverBorder = Color.FromArgb(153, 199, 204, 214),
        PopoverBorderWidth = 1,
        PopoverCornerRadius = 16,
        TitleBarBg = Color.FromArgb(255, 224, 230, 237),
        TitleText = Color.FromArgb(255, 77, 77, 89),
        TitleFormat = TitleFormat.LowercaseTilde,
        TitleFontFamily = "Segoe UI Variable",
        TitleFontSize = 12,
        SeparatorColor = Color.FromArgb(102, 204, 209, 217),
        FontFamily = "Segoe UI Variable",
        FontSize = 12,
        FontBoldFamily = "Segoe UI Variable",
        TextPrimary = Color.FromArgb(255, 38, 38, 51),
        TextDim = Color.FromArgb(255, 128, 128, 140),
        AccentColor = Color.FromArgb(255, 0, 120, 214),
        ErrorColor = Color.FromArgb(255, 217, 51, 38),
        SuccessColor = Color.FromArgb(255, 51, 166, 77),
        InputBg = Color.FromArgb(255, 255, 255, 255),
        InputCornerRadius = 8,
        BubbleBg = Color.FromArgb(242, 240, 242, 247),
        BubbleBorder = Color.FromArgb(102, 0, 120, 214),
        BubbleText = Color.FromArgb(255, 115, 120, 133),
        BubbleCompletionBorder = Color.FromArgb(153, 51, 179, 77),
        BubbleCompletionText = Color.FromArgb(255, 38, 140, 51),
        BubbleFontFamily = "Segoe UI Variable",
        BubbleFontSize = 10,
        BubbleCornerRadius = 12
    };

    private static readonly PopoverTheme _moss = new()
    {
        Name = "Moss",
        PopoverBg = Color.FromArgb(250, 209, 214, 199),
        PopoverBorder = Color.FromArgb(204, 140, 148, 128),
        PopoverBorderWidth = 2,
        PopoverCornerRadius = 10,
        TitleBarBg = Color.FromArgb(255, 184, 191, 173),
        TitleText = Color.FromArgb(255, 38, 43, 31),
        TitleFormat = TitleFormat.Capitalized,
        TitleFontFamily = "Segoe UI Variable",
        TitleFontSize = 11,
        SeparatorColor = Color.FromArgb(128, 140, 148, 128),
        FontFamily = "Consolas",
        FontSize = 11,
        FontBoldFamily = "Consolas",
        TextPrimary = Color.FromArgb(255, 26, 31, 20),
        TextDim = Color.FromArgb(255, 89, 97, 77),
        AccentColor = Color.FromArgb(255, 51, 56, 38),
        ErrorColor = Color.FromArgb(255, 153, 38, 26),
        SuccessColor = Color.FromArgb(255, 38, 102, 38),
        InputBg = Color.FromArgb(255, 224, 230, 214),
        InputCornerRadius = 3,
        BubbleBg = Color.FromArgb(242, 209, 214, 199),
        BubbleBorder = Color.FromArgb(179, 140, 148, 128),
        BubbleText = Color.FromArgb(255, 102, 107, 97),
        BubbleCompletionBorder = Color.FromArgb(179, 51, 128, 51),
        BubbleCompletionText = Color.FromArgb(255, 38, 102, 38),
        BubbleFontFamily = "Consolas",
        BubbleFontSize = 10,
        BubbleCornerRadius = 8
    };

    /// <summary>
    /// Creates a Peach theme variant tinted with the given character color.
    /// Port of withCharacterColor() from PopoverTheme.swift.
    /// </summary>
    public PopoverTheme WithCharacterColor(Color color)
    {
        if (Name != "Peach") return this;
        byte r = color.R, g = color.G, b = color.B;
        return this with
        {
            PopoverBorder = Color.FromArgb(153, r, g, b),
            TitleBarBg = Color.FromArgb(255,
                (byte)Math.Min(r * 0.3 + 0.7 * 255, 255),
                (byte)Math.Min(g * 0.3 + 0.7 * 255, 255),
                (byte)Math.Min(b * 0.3 + 0.7 * 255, 255)),
            TitleText = color,
            SeparatorColor = Color.FromArgb(64,
                (byte)Math.Min(r + 102, 255),
                (byte)Math.Min(g + 102, 255),
                (byte)Math.Min(b + 102, 255)),
            AccentColor = color,
            BubbleBg = Color.FromArgb(242,
                (byte)Math.Min(r * 0.15 + 0.85 * 255, 255),
                (byte)Math.Min(g * 0.15 + 0.85 * 255, 255),
                (byte)Math.Min(b * 0.15 + 0.85 * 255, 255)),
            BubbleBorder = Color.FromArgb(153, r, g, b),
        };
    }
}
