namespace LilAgents.Core;

public enum ChatMessageRole
{
    User,
    Assistant,
    Error,
    ToolUse,
    ToolResult
}

public sealed record ChatMessage(ChatMessageRole Role, string Text);
