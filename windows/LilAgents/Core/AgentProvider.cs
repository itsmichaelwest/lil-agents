using Microsoft.UI.Dispatching;

namespace LilAgents.Core;

/// <summary>
/// Supported agent CLI providers.
/// Windows equivalent of AgentSession.swift's AgentProvider enum.
/// </summary>
public enum AgentProvider
{
    Claude,
    Codex,
    Copilot,
    Gemini,
}

/// <summary>
/// Title format for provider display strings.
/// </summary>
public enum TitleFormat
{
    Uppercase,
    LowercaseTilde,
    Capitalized,
}

/// <summary>
/// Extension methods for <see cref="AgentProvider"/>.
/// </summary>
public static class AgentProviderExtensions
{
    /// <summary>Human-readable display name.</summary>
    public static string DisplayName(this AgentProvider provider) => provider switch
    {
        AgentProvider.Claude => "Claude",
        AgentProvider.Codex => "Codex",
        AgentProvider.Copilot => "Copilot",
        AgentProvider.Gemini => "Gemini",
        _ => provider.ToString(),
    };

    /// <summary>Placeholder text for the chat input field.</summary>
    public static string InputPlaceholder(this AgentProvider provider) => provider switch
    {
        AgentProvider.Claude => "Ask Claude...",
        AgentProvider.Codex => "Ask Codex...",
        AgentProvider.Copilot => "Ask Copilot...",
        AgentProvider.Gemini => "Ask Gemini...",
        _ => "Ask...",
    };

    /// <summary>Formatted title string for display in various contexts.</summary>
    public static string TitleString(this AgentProvider provider, TitleFormat format)
    {
        var name = provider.DisplayName();
        return format switch
        {
            TitleFormat.Uppercase => name.ToUpperInvariant(),
            TitleFormat.LowercaseTilde => $"{name.ToLowerInvariant()} ~",
            TitleFormat.Capitalized => name,
            _ => name,
        };
    }

    /// <summary>
    /// Windows-specific install instructions for the provider's CLI tool.
    /// </summary>
    public static string InstallInstructions(this AgentProvider provider) => provider switch
    {
        AgentProvider.Claude =>
            "Install Claude Code from:\n"
            + "  https://claude.ai/download\n\n"
            + "Or via winget:\n"
            + "  winget install Anthropic.Claude",

        AgentProvider.Codex =>
            "Install Codex CLI:\n"
            + "  npm install -g @openai/codex",

        AgentProvider.Copilot =>
            "Install Copilot CLI:\n"
            + "  npm install -g @github/copilot-cli",

        AgentProvider.Gemini =>
            "Install Gemini CLI:\n"
            + "  npm install -g @google/gemini-cli\n\n"
            + "Then authenticate:\n"
            + "  gemini auth",

        _ => "Unknown provider.",
    };

    /// <summary>
    /// Factory method that creates a new <see cref="IChatSession"/> for the provider.
    /// </summary>
    public static IChatSession CreateSession(this AgentProvider provider, DispatcherQueue dispatcher) =>
        provider switch
        {
            AgentProvider.Claude => new ClaudeSession(dispatcher),
            AgentProvider.Codex => new CodexSession(dispatcher),
            AgentProvider.Copilot => new CopilotSession(dispatcher),
            AgentProvider.Gemini => new GeminiSession(dispatcher),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported agent provider."),
        };

    /// <summary>
    /// Gets or sets the currently selected provider, backed by <see cref="App.Settings"/>.
    /// </summary>
    public static AgentProvider Current
    {
        get => Enum.TryParse<AgentProvider>(App.Settings.SelectedProvider, ignoreCase: true, out var p)
            ? p
            : AgentProvider.Claude;
        set => App.Settings.SelectedProvider = value.ToString();
    }
}
