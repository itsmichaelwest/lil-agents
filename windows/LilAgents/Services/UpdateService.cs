using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace LilAgents.Services;

/// <summary>
/// Checks GitHub Releases for a newer version of the app.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private const string ReleasesUrl =
        "https://api.github.com/repos/anthropics/lil-agents/releases/latest";

    private readonly HttpClient _http;
    private bool _disposed;

    public UpdateService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "lil-agents-windows");
        _http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    }

    /// <summary>
    /// Result of a version check.
    /// </summary>
    public sealed record UpdateCheckResult(
        bool UpdateAvailable,
        string? LatestVersion,
        string? DownloadUrl,
        string? ReleaseNotes);

    /// <summary>
    /// Checks the GitHub Releases API for a newer version.
    /// Returns null on network/parse failure (non-fatal).
    /// </summary>
    public async Task<UpdateCheckResult?> CheckForUpdateAsync(
        CancellationToken ct = default)
    {
        try
        {
            var release = await _http.GetFromJsonAsync<GitHubRelease>(
                ReleasesUrl, ct);

            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                return null;

            var latestVersion = ParseVersion(release.TagName);
            var currentVersion = GetCurrentVersion();

            if (latestVersion is null || currentVersion is null)
                return null;

            bool hasUpdate = latestVersion > currentVersion;

            // Find Windows asset URL, falling back to the HTML release page
            string? downloadUrl = null;
            if (release.Assets is { Length: > 0 })
            {
                foreach (var asset in release.Assets)
                {
                    if (asset.Name?.Contains("windows", StringComparison.OrdinalIgnoreCase) == true
                        || asset.Name?.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) == true
                        || asset.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        downloadUrl = asset.BrowserDownloadUrl;
                        break;
                    }
                }
            }
            downloadUrl ??= release.HtmlUrl;

            return new UpdateCheckResult(
                hasUpdate,
                release.TagName,
                downloadUrl,
                release.Body);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Network errors, JSON parse errors, etc. are non-fatal.
            return null;
        }
    }

    private static Version? GetCurrentVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version;
        return ver ?? new Version(1, 0, 0);
    }

    private static Version? ParseVersion(string tag)
    {
        // Strip leading 'v' (e.g. "v1.2.3" -> "1.2.3")
        var s = tag.TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? v : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }

    // --- JSON DTOs ---

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
