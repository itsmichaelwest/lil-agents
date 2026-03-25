using System.Diagnostics;
using System.IO;

namespace LilAgents.Services;

/// <summary>
/// Locates the Claude CLI executable on Windows.
/// Unlike macOS, Windows processes inherit the full user PATH,
/// so no shell environment capture is needed.
/// </summary>
public static class ClaudePathResolver
{
    private static string? _cachedPath;

    /// <summary>
    /// Returns the path to claude.exe, or null if not found.
    /// Result is cached after first successful resolution.
    /// </summary>
    public static string? Resolve()
    {
        if (_cachedPath is not null && File.Exists(_cachedPath))
            return _cachedPath;

        _cachedPath = FindOnPath()
                      ?? CheckKnownLocations();
        return _cachedPath;
    }

    /// <summary>Clears the cached path so next Resolve() re-scans.</summary>
    public static void ClearCache() => _cachedPath = null;

    /// <summary>
    /// Uses the system PATH to locate claude.exe (equivalent to `where claude`).
    /// </summary>
    private static string? FindOnPath()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "claude",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return null;

            // `where` can return multiple lines; take the first .exe match.
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (File.Exists(line))
                    return line;
            }
        }
        catch
        {
            // `where` not available or failed; fall through to known locations.
        }

        return null;
    }

    /// <summary>
    /// Checks well-known Windows install locations for claude.exe.
    /// </summary>
    private static string? CheckKnownLocations()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        string[] candidates =
        [
            Path.Combine(localAppData, "Programs", "claude", "claude.exe"),
            Path.Combine(userProfile, ".claude", "local", "bin", "claude.exe"),
            Path.Combine(programFiles, "Claude", "claude.exe"),
            Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "claude.exe"),
        ];

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }
}
