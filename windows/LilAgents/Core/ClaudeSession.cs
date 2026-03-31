using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LilAgents.Services;
using Microsoft.UI.Dispatching;

namespace LilAgents.Core;

/// <summary>
/// Manages a Claude Code CLI subprocess using the NDJSON streaming protocol.
/// Port of ClaudeSession.swift to C# / System.Diagnostics.Process.
///
/// All public events fire on the UI thread via DispatcherQueue.
/// </summary>
public sealed class ClaudeSession : IChatSession, IDisposable
{
    private Process? _process;
    private StreamWriter? _stdin;
    private readonly StringBuilder _lineBuffer = new();
    private readonly List<ChatMessage> _history = [];
    private readonly DispatcherQueue _dispatcher;
    private readonly StringBuilder _streamedText = new();
    private bool _disposed;

    public ClaudeSession(DispatcherQueue dispatcher)
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
        var claudePath = BinaryResolver.Resolve("claude");
        if (claudePath is null)
        {
            var msg = "Claude CLI not found.\n\n"
                    + "Install Claude Code from:\n"
                    + "  https://claude.ai/download\n\n"
                    + "Or via winget:\n"
                    + "  winget install Anthropic.Claude";
            RaiseError(msg);
            _history.Add(new ChatMessage(ChatMessageRole.Error, msg));
            return;
        }

        LaunchProcess(claudePath);
    }

    public void Send(string message)
    {
        if (!IsRunning || _stdin is null) return;

        IsBusy = true;
        _streamedText.Clear();
        _history.Add(new ChatMessage(ChatMessageRole.User, message));

        var payload = new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = message,
            },
        };

        var json = payload.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        try
        {
            _stdin.WriteLine(json);
        }
        catch (Exception ex)
        {
            RaiseError($"Failed to send message: {ex.Message}");
        }
    }

    public void Terminate()
    {
        if (_process is not null && !_process.HasExited)
        {
            try { _process.Kill(); }
            catch { /* already dead */ }
        }
        IsRunning = false;
        IsBusy = false;
    }

    // ── Process launch ───────────────────────────────────────────────

    private void LaunchProcess(string claudePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = claudePath,
            Arguments = "-p --output-format stream-json --input-format stream-json --verbose --dangerously-skip-permissions",
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // Ensure TERM=dumb so Claude CLI does not emit ANSI escape codes.
        psi.Environment["TERM"] = "dumb";
        // Strip env vars that interfere with nested Claude CLI invocations.
        psi.Environment.Remove("CLAUDECODE");
        psi.Environment.Remove("CLAUDE_CODE_ENTRYPOINT");

        try
        {
            _process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            var msg = $"Failed to launch Claude CLI.\n\nError: {ex.Message}";
            RaiseError(msg);
            _history.Add(new ChatMessage(ChatMessageRole.Error, msg));
            return;
        }

        if (_process is null)
        {
            RaiseError("Failed to launch Claude CLI process.");
            return;
        }

        _stdin = _process.StandardInput;
        _stdin.AutoFlush = true;
        // Force LF line endings — NDJSON protocol uses \n, not \r\n.
        _stdin.NewLine = "\n";

        IsRunning = true;

        // Dedicated threads for stdout/stderr reading.
        // Using threads instead of async to avoid .NET Process I/O deadlocks.
        StartReadThread(_process.StandardOutput, ProcessOutputChunk);
        StartReadThread(_process.StandardError, RaiseError);

        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) =>
        {
            _dispatcher.TryEnqueue(() =>
            {
                IsRunning = false;
                IsBusy = false;
                ProcessExited?.Invoke();
            });
        };
    }

    /// <summary>
    /// Reads lines from a StreamReader in a background loop and dispatches each
    /// non-empty line to <paramref name="onLine"/> on the calling context.
    /// Uses a dedicated thread instead of async to avoid deadlocks with
    /// Process.StandardOutput — .NET's async Process I/O is unreliable.
    /// </summary>
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
            Name = $"ClaudeSession_{reader.GetHashCode():X}_Reader"
        };
        thread.Start();
    }

    // ── NDJSON line-buffer & parsing ─────────────────────────────────

    /// <summary>
    /// Receives a single complete line from stdout (already split by ReadLineAsync).
    /// </summary>
    private void ProcessOutputChunk(string line)
    {
        // ReadLineAsync already gives us one line at a time, so parse directly.
        ParseLine(line);
    }

    private void ParseLine(string line)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(line);
        }
        catch
        {
            return; // Not valid JSON; ignore.
        }

        if (root is null) return;

        var type = root["type"]?.GetValue<string>() ?? "";

        switch (type)
        {
            case "system":
                HandleSystemMessage(root);
                break;
            case "assistant":
                HandleAssistantMessage(root);
                break;
            case "user":
                HandleUserMessage(root);
                break;
            case "result":
                HandleResultMessage(root);
                break;
        }
    }

    private void HandleSystemMessage(JsonNode root)
    {
        var subtype = root["subtype"]?.GetValue<string>() ?? "";
        if (subtype == "init")
        {
            SessionReady?.Invoke();
        }
    }

    private void HandleAssistantMessage(JsonNode root)
    {
        var content = root["message"]?["content"]?.AsArray();
        if (content is null) return;

        foreach (var block in content)
        {
            if (block is null) continue;
            var blockType = block["type"]?.GetValue<string>() ?? "";

            if (blockType == "text")
            {
                var text = block["text"]?.GetValue<string>();
                if (text is not null)
                {
                    _streamedText.Append(text);
                    TextReceived?.Invoke(text);
                }
            }
            else if (blockType == "tool_use")
            {
                var toolName = block["name"]?.GetValue<string>() ?? "Tool";
                var input = ParseToolInput(block["input"]);
                var summary = FormatToolSummary(toolName, input);

                _history.Add(new ChatMessage(ChatMessageRole.ToolUse, $"{toolName}: {summary}"));
                ToolUsed?.Invoke(toolName, input);
            }
        }
    }

    private void HandleUserMessage(JsonNode root)
    {
        var content = root["message"]?["content"]?.AsArray();
        if (content is null) return;

        foreach (var block in content)
        {
            if (block is null) continue;
            if (block["type"]?.GetValue<string>() != "tool_result") continue;

            var isError = block["is_error"]?.GetValue<bool>() ?? false;
            var summary = ExtractToolResultSummary(root, block);

            var historyText = isError ? $"ERROR: {summary}" : summary;
            _history.Add(new ChatMessage(ChatMessageRole.ToolResult, historyText));
            ToolResultReceived?.Invoke(summary, isError);
        }
    }

    private void HandleResultMessage(JsonNode root)
    {
        IsBusy = false;

        // Prefer the accumulated streamed text over the result summary,
        // since the result field is often empty or truncated.
        var streamed = _streamedText.ToString();
        _streamedText.Clear();

        if (streamed.Length > 0)
        {
            _history.Add(new ChatMessage(ChatMessageRole.Assistant, streamed));
        }
        else
        {
            var result = root["result"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(result))
                _history.Add(new ChatMessage(ChatMessageRole.Assistant, result!));
        }

        TurnCompleted?.Invoke();
    }

    // ── Tool helpers ─────────────────────────────────────────────────

    private static Dictionary<string, object?> ParseToolInput(JsonNode? node)
    {
        var dict = new Dictionary<string, object?>();
        if (node is JsonObject obj)
        {
            foreach (var kvp in obj)
            {
                dict[kvp.Key] = kvp.Value?.ToString();
            }
        }
        return dict;
    }

    private static string ExtractToolResultSummary(JsonNode root, JsonNode block)
    {
        // Try structured tool_use_result first (file info).
        var resultInfo = root["tool_use_result"];
        if (resultInfo is JsonObject resultObj)
        {
            if (resultObj["type"]?.GetValue<string>() == "text")
            {
                var file = resultObj["file"];
                if (file is JsonObject fileObj)
                {
                    var path = fileObj["filePath"]?.GetValue<string>() ?? "";
                    var lines = 0;
                    try { lines = fileObj["totalLines"]?.GetValue<int>() ?? 0; } catch { }
                    if (!string.IsNullOrEmpty(path))
                        return $"{path} ({lines} lines)";
                }
            }
        }
        else if (resultInfo is not null)
        {
            // tool_use_result is a plain string.
            var str = resultInfo.GetValue<string>();
            if (!string.IsNullOrEmpty(str))
                return str.Length > 80 ? str[..80] : str;
        }

        // Fallback: content field on the block itself.
        var contentStr = block["content"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(contentStr))
            return contentStr!.Length > 80 ? contentStr[..80] : contentStr;

        return "";
    }

    /// <summary>
    /// Formats a human-readable summary for a tool use event.
    /// Mirrors formatToolSummary() from ClaudeSession.swift.
    /// </summary>
    private static string FormatToolSummary(string toolName, Dictionary<string, object?> input)
    {
        string? Get(string key) => input.TryGetValue(key, out var v) ? v?.ToString() : null;

        return toolName switch
        {
            "Bash" => Get("command") ?? "",
            "Read" => Get("file_path") ?? "",
            "Edit" or "Write" => Get("file_path") ?? "",
            "Glob" => Get("pattern") ?? "",
            "Grep" => Get("pattern") ?? "",
            _ => Get("description")
                 ?? string.Join(", ", input.Keys.OrderBy(k => k).Take(3)),
        };
    }

    public void ClearHistory() => _history.Clear();

    // ── Helpers ──────────────────────────────────────────────────────

    private void RaiseError(string message) => ErrorOccurred?.Invoke(message);

    // ── IDisposable ──────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Terminate();
        _stdin?.Dispose();
        _process?.Dispose();
    }
}
