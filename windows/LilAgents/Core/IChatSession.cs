namespace LilAgents.Core;

/// <summary>
/// Manages an agent CLI subprocess for chat interaction.
/// Handles NDJSON streaming protocol, tool use, and message history.
/// </summary>
public interface IChatSession
{
    bool IsBusy { get; }
    bool IsRunning { get; }
    IReadOnlyList<ChatMessage> History { get; }

    void Start();
    void Send(string message);
    void Terminate();
    void ClearHistory();

    event Action<string>? TextReceived;
    event Action? TurnCompleted;
    event Action<string>? ErrorOccurred;
    event Action<string, Dictionary<string, object?>>? ToolUsed;
    event Action<string, bool>? ToolResultReceived;
    event Action? SessionReady;
    event Action? ProcessExited;
}
