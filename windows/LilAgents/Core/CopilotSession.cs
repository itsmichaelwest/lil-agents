using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LilAgents.Services;
using Microsoft.UI.Dispatching;

namespace LilAgents.Core;

/// <summary>
/// Manages a GitHub Copilot CLI subprocess using the JSONL streaming protocol.
/// Port of CopilotSession.swift to C# / System.Diagnostics.Process.
///
/// Like Codex, Copilot is NOT a persistent process.
/// Each <see cref="Send"/> call launches a new process.
/// Subsequent turns use the <c>--continue</c> flag.
///
/// Falls back to plain text mode (<c>-s</c> flag) if JSON parsing fails.
///
/// All public events fire on the UI thread via DispatcherQueue.
/// </summary>
public sealed class CopilotSession : IChatSession, IDisposable
{
    private Process? _process;
    private readonly List<ChatMessage> _history = [];
    private readonly DispatcherQueue _dispatcher;
    private readonly StringBuilder _streamedText = new();
    private bool _disposed;
    private int _turnCount;
    private bool _useJsonMode = true;

    public CopilotSession(DispatcherQueue dispatcher)
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
        var copilotPath = BinaryResolver.Resolve("copilot");
        if (copilotPath is null)
        {
            var msg = $"Copilot CLI not found.\n\n{AgentProvider.Copilot.InstallInstructions()}";
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

        var copilotPath = BinaryResolver.Resolve("copilot");
        if (copilotPath is null)
        {
            RaiseError("Copilot CLI not found.");
            return;
        }

        IsBusy = true;
        _streamedText.Clear();
        _history.Add(new ChatMessage(ChatMessageRole.User, message));

        LaunchPerTurnProcess(copilotPath, message);
        _turnCount++;
    }

    public void Terminate()
    {
        KillCurrentProcess();
        IsRunning = false;
        IsBusy = false;
    }

    public void ClearHistory() => _history.Clear();

    // ── Per-turn process ─────────────────────────────────────────────

    private void LaunchPerTurnProcess(string copilotPath, string message)
    {
        KillCurrentProcess();

        var args = new StringBuilder();
        args.Append("-p ");
        args.Append(EscapeArgument(message));

        if (_useJsonMode)
            args.Append(" --output-format json");

        args.Append(" --allow-all");

        if (_turnCount > 0)
            args.Append(" --continue");

        var psi = new ProcessStartInfo
        {
            FileName = copilotPath,
            Arguments = args.ToString(),
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
            RaiseError($"Failed to launch Copilot CLI.\n\nError: {ex.Message}");
            return;
        }

        if (_process is null)
        {
            IsBusy = false;
            RaiseError("Failed to launch Copilot CLI process.");
            return;
        }

        var jsonParseFailed = false;

        StartReadThread(_process.StandardOutput, line =>
        {
            if (_useJsonMode && !jsonParseFailed)
            {
                if (!TryProcessJsonLine(line))
                {
                    // JSON parsing failed -- fall back to plain text for this
                    // and all future turns.
                    jsonParseFailed = true;
                    _useJsonMode = false;
                    HandlePlainTextLine(line);
                }
            }
            else
            {
                HandlePlainTextLine(line);
            }
        });

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

    /// <summary>
    /// Attempts to parse a line as JSON. Returns false if parsing fails.
    /// </summary>
    private bool TryProcessJsonLine(string line)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(line);
        }
        catch
        {
            return false;
        }

        if (root is null) return false;

        var type = root["type"]?.GetValue<string>() ?? "";
        // Copilot CLI nests payload under "data"; fall back to root for compat.
        var data = root["data"] ?? root;

        switch (type)
        {
            case "assistant.message":
            {
                var text = data["content"]?.GetValue<string>();
                if (text is not null)
                {
                    _streamedText.Append(text);
                    TextReceived?.Invoke(text);
                }
                break;
            }

            case "assistant.message_delta":
            {
                var delta = data["deltaContent"]?.GetValue<string>();
                if (delta is not null)
                {
                    _streamedText.Append(delta);
                    TextReceived?.Invoke(delta);
                }
                break;
            }

            case "assistant.turn_end":
                FinishTurn();
                break;

            case "result":
                FinishTurn();
                break;

            case "assistant.tool_call":
            {
                var toolName = data["name"]?.GetValue<string>()
                               ?? data["tool"]?.GetValue<string>()
                               ?? "Tool";
                var input = ParseToolInput(data["input"] ?? data["arguments"]);
                var summary = FormatToolSummary(toolName, input);

                _history.Add(new ChatMessage(ChatMessageRole.ToolUse, $"{toolName}: {summary}"));
                ToolUsed?.Invoke(toolName, input);
                break;
            }

            case "assistant.tool_result":
            {
                var output = data["output"]?.GetValue<string>()
                             ?? data["content"]?.GetValue<string>()
                             ?? "";
                var isError = data["is_error"]?.GetValue<bool>() ?? false;
                var summary = output.Length > 80 ? output[..80] : output;

                _history.Add(new ChatMessage(ChatMessageRole.ToolResult, isError ? $"ERROR: {summary}" : summary));
                ToolResultReceived?.Invoke(summary, isError);
                break;
            }

            case "error":
            {
                var errMsg = data["message"]?.GetValue<string>()
                             ?? data["error"]?.GetValue<string>()
                             ?? "Unknown error.";
                RaiseError(errMsg);
                break;
            }

            default:
                // Unknown/ephemeral type -- ignore but still valid JSON.
                break;
        }

        return true;
    }

    private void HandlePlainTextLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        _streamedText.Append(line);
        _streamedText.Append('\n');
        TextReceived?.Invoke(line + "\n");
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
            Name = $"CopilotSession_{reader.GetHashCode():X}_Reader",
        };
        thread.Start();
    }

    private static Dictionary<string, object?> ParseToolInput(JsonNode? node)
    {
        var dict = new Dictionary<string, object?>();
        if (node is JsonObject obj)
        {
            foreach (var kvp in obj)
                dict[kvp.Key] = kvp.Value?.ToString();
        }
        return dict;
    }

    private static string FormatToolSummary(string toolName, Dictionary<string, object?> input)
    {
        string? Get(string key) => input.TryGetValue(key, out var v) ? v?.ToString() : null;

        return toolName switch
        {
            "Bash" or "bash" or "shell" => Get("command") ?? "",
            "Read" or "read" => Get("file_path") ?? "",
            "Edit" or "Write" or "edit" or "write" => Get("file_path") ?? "",
            _ => Get("description") ?? string.Join(", ", input.Keys.OrderBy(k => k).Take(3)),
        };
    }

    private static string EscapeArgument(string arg) =>
        "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

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
