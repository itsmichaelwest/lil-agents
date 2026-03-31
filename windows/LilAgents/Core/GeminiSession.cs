using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LilAgents.Services;
using Microsoft.UI.Dispatching;

namespace LilAgents.Core;

/// <summary>
/// Manages a Google Gemini CLI subprocess.
/// Port of GeminiSession.swift to C# / System.Diagnostics.Process.
///
/// Like Codex and Copilot, Gemini is NOT a persistent process.
/// Each <see cref="Send"/> call launches a new process.
/// Subsequent turns use the <c>--continue</c> flag.
///
/// Gemini can output JSONL or plain text depending on version.
/// Tries JSONL parsing first, falls back to plain text streaming.
/// Filters stderr noise (progress spinners, checkmarks, arrows).
///
/// All public events fire on the UI thread via DispatcherQueue.
/// </summary>
public sealed class GeminiSession : IChatSession, IDisposable
{
    private Process? _process;
    private readonly List<ChatMessage> _history = [];
    private readonly DispatcherQueue _dispatcher;
    private readonly StringBuilder _streamedText = new();
    private bool _disposed;
    private int _turnCount;

    /// <summary>
    /// Regex to filter out Gemini CLI spinner/progress noise from stderr.
    /// Matches lines containing common spinner characters, checkmarks, arrows, etc.
    /// </summary>
    private static readonly Regex StderrNoisePattern = new(
        @"^[\s]*[✓✔✗✘⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏→←↑↓►▶▷▸●◉◎○◯⬤⏳⌛…\.\-\|/\\]+[\s]*$",
        RegexOptions.Compiled);

    public GeminiSession(DispatcherQueue dispatcher)
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
        var geminiPath = BinaryResolver.Resolve("gemini");
        if (geminiPath is null)
        {
            var msg = $"Gemini CLI not found.\n\n{AgentProvider.Gemini.InstallInstructions()}";
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

        var geminiPath = BinaryResolver.Resolve("gemini");
        if (geminiPath is null)
        {
            RaiseError("Gemini CLI not found.");
            return;
        }

        IsBusy = true;
        _streamedText.Clear();
        _history.Add(new ChatMessage(ChatMessageRole.User, message));

        LaunchPerTurnProcess(geminiPath, message);
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

    private void LaunchPerTurnProcess(string geminiPath, string message)
    {
        KillCurrentProcess();

        var args = new StringBuilder();
        args.Append("--yolo -p ");
        args.Append(EscapeArgument(message));

        if (_turnCount > 0)
            args.Append(" --continue");

        var psi = new ProcessStartInfo
        {
            FileName = geminiPath,
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
            RaiseError($"Failed to launch Gemini CLI.\n\nError: {ex.Message}");
            return;
        }

        if (_process is null)
        {
            IsBusy = false;
            RaiseError("Failed to launch Gemini CLI process.");
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

    // ── Output parsing (JSONL with plain text fallback) ──────────────

    private void ProcessOutputLine(string line)
    {
        // Try JSONL parsing first.
        if (TryProcessJsonLine(line))
            return;

        // Fall back to plain text.
        HandlePlainTextLine(line);
    }

    /// <summary>
    /// Attempts to parse a line as JSON. Returns true if it was valid JSON.
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

        switch (type)
        {
            // Text content events -- Gemini uses various field names across versions.
            case "content":
            case "text":
            case "delta":
            case "message":
            {
                var text = root["text"]?.GetValue<string>()
                           ?? root["content"]?.GetValue<string>()
                           ?? root["delta"]?.GetValue<string>()
                           ?? root["message"]?.GetValue<string>();
                if (text is not null)
                {
                    _streamedText.Append(text);
                    TextReceived?.Invoke(text);
                }
                break;
            }

            // Tool call events.
            case "tool_call":
            case "function_call":
            {
                var toolName = root["name"]?.GetValue<string>()
                               ?? root["function"]?.GetValue<string>()
                               ?? "Tool";
                var input = ParseToolInput(root["arguments"] ?? root["input"] ?? root["parameters"]);
                var summary = FormatToolSummary(toolName, input);

                _history.Add(new ChatMessage(ChatMessageRole.ToolUse, $"{toolName}: {summary}"));
                ToolUsed?.Invoke(toolName, input);
                break;
            }

            // Tool result events.
            case "tool_result":
            case "function_result":
            {
                var output = root["output"]?.GetValue<string>()
                             ?? root["result"]?.GetValue<string>()
                             ?? root["content"]?.GetValue<string>()
                             ?? "";
                var isError = root["is_error"]?.GetValue<bool>()
                              ?? root["error"]?.GetValue<bool>()
                              ?? false;
                var summary = output.Length > 80 ? output[..80] : output;

                _history.Add(new ChatMessage(ChatMessageRole.ToolResult, isError ? $"ERROR: {summary}" : summary));
                ToolResultReceived?.Invoke(summary, isError);
                break;
            }

            // Turn completion events.
            case "done":
            case "end":
            case "complete":
            case "turn_end":
                FinishTurn();
                break;

            // Error events.
            case "error":
            {
                var errMsg = root["message"]?.GetValue<string>()
                             ?? root["error"]?.GetValue<string>()
                             ?? "Unknown error.";
                RaiseError(errMsg);
                break;
            }

            default:
                // Unknown type but valid JSON -- check for inline text fields.
                var inlineText = root["text"]?.GetValue<string>()
                                 ?? root["content"]?.GetValue<string>();
                if (inlineText is not null)
                {
                    _streamedText.Append(inlineText);
                    TextReceived?.Invoke(inlineText);
                }
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

        // Filter out progress spinner noise that Gemini CLI emits.
        if (StderrNoisePattern.IsMatch(line)) return;

        // Also filter lines that are only whitespace + ANSI escape codes.
        var stripped = Regex.Replace(line, @"\x1B\[[0-9;]*[A-Za-z]", "").Trim();
        if (stripped.Length == 0) return;

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
            Name = $"GeminiSession_{reader.GetHashCode():X}_Reader",
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
