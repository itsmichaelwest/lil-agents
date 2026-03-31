using System.Diagnostics;
using System.IO;

namespace LilAgents.Services;

/// <summary>
/// Locates CLI executables on Windows. Searches the inherited PATH via
/// <c>where.exe</c>, then falls back to well-known install locations. If the
/// initial search fails, re-reads the live User+System PATH from the
/// registry so tools installed after app launch can still be found.
///
/// Results are cached per binary name. Call <see cref="ClearCache()"/> if
/// you want to force a re-scan (e.g. after the user says "I just installed it").
/// </summary>
public static class BinaryResolver
{
    private static readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the full path to an executable, or null if not found.
    /// Results are cached after the first successful resolution per name.
    /// </summary>
    public static string? Resolve(string name, string[]? extraFallbackPaths = null)
    {
        // Return cached result if still valid
        if (_cache.TryGetValue(name, out var cached))
        {
            if (cached is not null && File.Exists(cached) && HasExecutableExtension(cached))
                return cached;
            _cache.Remove(name); // stale cache entry
        }

        // 1. Try `where.exe` against the inherited PATH
        var found = FindViaWhere(name);

        // 2. Try well-known Windows install locations
        if (found is null)
            found = SearchKnownLocations(name, extraFallbackPaths);

        // 3. If still not found, re-read the live PATH from the registry
        //    (catches tools installed after app launch)
        if (found is null)
            found = SearchLivePath(name);

        _cache[name] = found;
        return found;
    }

    /// <summary>Clears all cached paths so next Resolve() re-scans.</summary>
    public static void ClearCache() => _cache.Clear();

    /// <summary>Clears the cached path for a specific binary name.</summary>
    public static void ClearCache(string name) => _cache.Remove(name);

    // ── where.exe ─────────────────────────────────────────────────

    private static string? FindViaWhere(string name)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = name,
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

            // `where` can return multiple lines; take the first with a valid
            // Windows executable extension (.exe, .cmd, .bat). Extensionless
            // entries are Unix shell scripts that Process.Start cannot run.
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (File.Exists(line) && HasExecutableExtension(line))
                    return line;
            }
        }
        catch { }

        return null;
    }

    // ── Known locations ───────────────────────────────────────────

    private static string? SearchKnownLocations(string name, string[]? extraPaths)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        // Build the candidate list. The exact names differ per tool,
        // but all npm-installed CLIs end up as .cmd shims.
        var exeName = name + ".exe";
        var cmdName = name + ".cmd";

        var candidates = new List<string>
        {
            // Direct installer (e.g. Claude, standalone tools)
            Path.Combine(localAppData, "Programs", name, exeName),
            Path.Combine(programFiles, char.ToUpper(name[0]) + name[1..], exeName),

            // npm global (default location)
            Path.Combine(appData, "npm", cmdName),
            Path.Combine(appData, "npm", exeName),

            // npm global (custom prefix via npmrc)
            Path.Combine(userProfile, ".npm-global", cmdName),
            Path.Combine(userProfile, ".npm-global", exeName),

            // npm via nvm-windows
            Path.Combine(appData, "nvm", cmdName),

            // Scoop
            Path.Combine(userProfile, "scoop", "shims", exeName),
            Path.Combine(userProfile, "scoop", "shims", cmdName),

            // Chocolatey
            Path.Combine(programData, "chocolatey", "bin", exeName),
            Path.Combine(programData, "chocolatey", "bin", cmdName),

            // Volta
            Path.Combine(localAppData, "Volta", "bin", exeName),
            Path.Combine(localAppData, "Volta", "bin", cmdName),

            // winget links
            Path.Combine(localAppData, "Microsoft", "WinGet", "Links", exeName),

            // User .local/bin (common for manual installs)
            Path.Combine(userProfile, ".local", "bin", exeName),

            // Claude-specific: .claude/local/bin
            Path.Combine(userProfile, ".claude", "local", "bin", exeName),
        };

        // Add any tool-specific extra paths
        if (extraPaths is not null)
            candidates.AddRange(extraPaths);

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    // ── Live PATH from registry ───────────────────────────────────

    /// <summary>
    /// Re-reads the User and System PATH from the registry (not the
    /// inherited environment). This catches tools installed after the
    /// app was launched, since Windows doesn't update running processes'
    /// environment variables.
    /// </summary>
    private static string? SearchLivePath(string name)
    {
        try
        {
            var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
            var systemPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";
            var livePath = userPath + ";" + systemPath;

            var exeName = name + ".exe";
            var cmdName = name + ".cmd";

            foreach (var dir in livePath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var exeCandidate = Path.Combine(dir, exeName);
                if (File.Exists(exeCandidate))
                    return exeCandidate;

                var cmdCandidate = Path.Combine(dir, cmdName);
                if (File.Exists(cmdCandidate))
                    return cmdCandidate;
            }
        }
        catch { }

        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static bool HasExecutableExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase);
    }
}
