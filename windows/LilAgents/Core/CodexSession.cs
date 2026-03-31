using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LilAgents.Services;
using Microsoft.UI.Dispatching;

namespace LilAgents.Core;

/// <summary>
/// Manages a Codex CLI subprocess using the JSONL streaming protocol.
/// Port of CodexSession.swift to C# / System.Diagnostics.Process.
///
/// Unlike ClaudeSession, Codex is NOT a persistent process.
/// Each <see cref="Send"/> call launches a new <c>codex exec</c> process.
/// Multi-turn context is provided by prepending history to the prompt.
///
/// All public events fire on the UI thread via DispatcherQueue.
/// </summary>
public sealed class CodexSession : IChatSession, IDisposable
{
    private Process? _process;
    private readonly List<ChatMessage> _history = [];
    private readonly DispatcherQueue _dispatcher;
    private readonly StringBuilder _streamedText = new();
    private bool _disposed;

    public CodexSession(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    // ── IChatSession properties ──────────────────────────────────────

    public bool IsBusy { get; private set; }
    public bool IsRunning { get; private set; }
    public IReadOnlyList<ChatMessage> History => _history;

    // ── IChatSession events ──────────────────────────────────────────

    public event Action<string>? TextReceived;
    public event Action? TurnCompleted;
    public event Action<string>? ErrorOccurred;
    public event Action<string, Dictionary<string, object?>>? ToolUsed;
    public event Action<string, bool>? ToolResultReceived;
    public event Action? SessionReady;
    public event Action? ProcessExited;

    // ── Lifecycle ────────────────────────────────────────────────────

    public void Start()
    {
        var codexPath = BinaryResolver.Resolve("codex");
        if (codexPath is null)
        {
            var msg = $"Codex CLI not found.\n\n{AgentProvider.Codex.InstallInstructions()}";
            RaiseError(msg);
            _history.Add(new ChatMessage(ChatMessageRole.Error, msg));
            return;
        }

        IsRunning = true;
        SessionReady?.Invoke();
    }

    public void Send(string message)
    {
        if (!IsRunning) return;

        var codexPath = BinaryResolver.Resolve("codex");
        if (codexPath is null)
        {
            RaiseError("Codex CLI not found.");
            return;
        }

        IsBusy = true;
        _streamedText.Clear();
        _history.Add(new ChatMessage(ChatMessageRole.User, message));

        // Build prompt with history context for multi-turn.
        var prompt = BuildPromptWithHistory(message);

        LaunchPerTurnProcess(codexPath, prompt);
    }

    public void Terminate()
    {
        KillCurrentProcess();
        IsRunning = false;
        IsBusy = false;
    }

    public void ClearHistory() => _history.Clear();

    // ── Multi-turn prompt ────────────────────────────────────────────

    private string BuildPromptWithHistory(string currentMessage)
    {
        if (_history.Count <= 1)
            return currentMessage;

        var sb = new StringBuilder();
        sb.AppendLine("Previous conversation context:");
        sb.AppendLine("---");

        // Include all history except the last message (which is the current one).
        for (int i = 0; i < _history.Count - 1; i++)
        {
            var msg = _history[i];
            var prefix = msg.Role switch
            {
                ChatMessageRole.User => "User",
                ChatMessageRole.Assistant => "Assistant",
                ChatMessageRole.ToolUse => "Tool",
                ChatMessageRole.ToolResult => "Result",
                _ => "System",
            };
            sb.AppendLine($"{prefix}: {msg.Text}");
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"Current request: {currentMessage}");
        return sb.ToString();
    }

    // ── Per-turn process ─────────────────────────────────────────────

    private void LaunchPerTurnProcess(string codexPath, string prompt)
    {
        KillCurrentProcess();

        var psi = new ProcessStartInfo
        {
            FileName = codexPath,
            Arguments = $"exec --json --full-auto --skip-git-repo-check {EscapeArgument(prompt)}",
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        psi.Environment["TERM"] = "dumb";

        try
        {
            _process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            IsBusy = false;
            RaiseError($"Failed to launch Codex CLI.\n\nError: {ex.Message}");
            return;
        }

        if (_process is null)
        {
            IsBusy = false;
            RaiseError("Failed to launch Codex CLI process.");
            return;
        }

        StartReadThread(_process.StandardOutput, ProcessOutputLine);
        StartReadThread(_process.StandardError, HandleStderrLine);

        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) =>
        {
            _dispatcher.TryEnqueue(() =>
            {
                FinishTurn();
                ProcessExited?.Invoke();
            });
        };
    }

    // ── JSONL parsing ────────────────────────────────────────────────

    private void ProcessOutputLine(string line)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(line);
        }
        catch
        {
            // Not JSON -- treat as plain text output.
            if (!string.IsNullOrWhiteSpace(line))
            {
                _streamedText.Append(line);
                TextReceived?.Invoke(line);
            }
            return;
        }

        if (root is null) return;

        var type = root["type"]?.GetValue<string>() ?? "";

        switch (type)
        {
            case "thread.started":
                // Session started, no action needed.
                break;

            case "item.started":
                HandleItemStarted(root);
                break;

            case "item.completed":
                HandleItemCompleted(root);
                break;

            case "turn.completed":
                FinishTurn();
                break;

            case "turn.failed":
                var failMsg = root["error"]?.GetValue<string>()
                              ?? root["message"]?.GetValue<string>()
                              ?? "Turn failed.";
                RaiseError(failMsg);
                FinishTurn();
                break;

            case "error":
                var errMsg = root["message"]?.GetValue<string>()
                             ?? root["error"]?.GetValue<string>()
                             ?? "Unknown error.";
                RaiseError(errMsg);
                break;
        }
    }

    private void HandleItemStarted(JsonNode root)
    {
        var itemType = root["item"]?["type"]?.GetValue<string>() ?? "";
        if (itemType == "command_execution")
        {
            var command = root["item"]?["command"]?.GetValue<string>() ?? "command";
            var input = new Dictionary<string, object?> { ["command"] = command };
            _history.Add(new ChatMessage(ChatMessageRole.ToolUse, $"Bash: {command}"));
            ToolUsed?.Invoke("Bash", input);
        }
    }

    private void HandleItemCompleted(JsonNode root)
    {
        var itemType = root["item"]?["type"]?.GetValue<string>() ?? "";

        switch (itemType)
        {
            case "agent_message":
            {
                var text = root["item"]?["content"]?.GetValue<string>()
                           ?? root["item"]?["text"]?.GetValue<string>();
                if (text is not null)
                {
                    _streamedText.Append(text);
                    TextReceived?.Invoke(text);
                }
                break;
            }

            case "command_execution":
            {
                var exitCode = 0;
                try { exitCode = root["item"]?["exit_code"]?.GetValue<int>() ?? 0; } catch { }
                var output = root["item"]?["output"]?.GetValue<string>() ?? "";
                var isError = exitCode != 0;
                var summary = output.Length > 80 ? output[..80] : output;

                _history.Add(new ChatMessage(ChatMessageRole.ToolResult, isError ? $"ERROR: {summary}" : summary));
                ToolResultReceived?.Invoke(summary, isError);
                break;
            }

            case "file_change":
            {
                var filePath = root["item"]?["file_path"]?.GetValue<string>() ?? "file";
                var action = root["item"]?["action"]?.GetValue<string>() ?? "changed";
                var summary = $"{action}: {filePath}";
                var input = new Dictionary<string, object?> { ["file_path"] = filePath, ["action"] = action };

                _history.Add(new ChatMessage(ChatMessageRole.ToolUse, summary));
                ToolUsed?.Invoke("FileChange", input);
                break;
            }
        }
    }

    private void HandleStderrLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        RaiseError(line);
    }

    // ── Turn completion ──────────────────────────────────────────────

    private void FinishTurn()
    {
        if (!IsBusy) return;

        IsBusy = false;
        var text = _streamedText.ToString();
        _streamedText.Clear();

        if (text.Length > 0)
        {
            _history.Add(new ChatMessage(ChatMessageRole.Assistant, text));
        }

        TurnCompleted?.Invoke();
    }

    // ── Process management ───────────────────────────────────────────

    private void KillCurrentProcess()
    {
        if (_process is not null && !_process.HasExited)
        {
            try { _process.Kill(); }
            catch { /* already dead */ }
        }
        _process = null;
    }

    // ── Shared helpers ───────────────────────────────────────────────

    private void StartReadThread(StreamReader reader, Action<string> onLine)
    {
        var thread = new Thread(() =>
        {
            try
            {
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (line.Length == 0) continue;
                    var captured = line;
                    _dispatcher.TryEnqueue(() => onLine(captured));
                }
            }
            catch (ObjectDisposedException) { /* process killed */ }
            catch (InvalidOperationException) { /* stream closed */ }
        })
        {
            IsBackground = true,
            Name = $"CodexSession_{reader.GetHashCode():X}_Reader",
        };
        thread.Start();
    }

    private static string EscapeArgument(string arg)
    {
        // Wrap in double quotes and escape inner double quotes for Windows cmd.
        return "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private void RaiseError(string message) => ErrorOccurred?.Invoke(message);

    // ── IDisposable ──────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Terminate();
        _process?.Dispose();
    }
}
