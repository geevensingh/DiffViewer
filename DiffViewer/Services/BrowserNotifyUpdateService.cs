using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// <see cref="IUpdateService"/> for portable launches — the case
/// where DiffViewer is running from a single-file
/// <c>DiffViewer-portable.exe</c> (or <c>dotnet run</c> during
/// development on a developer's machine that happens to set
/// <c>useDispatcherTimer=true</c>, though that's mostly hypothetical).
///
/// <para>Velopack's <see cref="Velopack.UpdateManager"/> only works
/// inside a Velopack-installed location, so for portable copies we
/// hit the GitHub Releases REST API directly, compare the highest
/// non-draft release to our running assembly version, and surface
/// the notification via the Phase 2.3 banner. The "Install" action
/// opens the Releases page in the user's default browser
/// — they download <c>DiffViewer-Setup.exe</c> (or the new
/// portable) and decide what to do.</para>
///
/// <para>This is <see cref="CanAutoApply"/> = <c>false</c> precisely
/// because "apply" launches a browser tab, which would be hostile
/// UX if it fired unprompted from a background update check. The
/// <see cref="DiffViewer.ViewModels.UpdateNotificationViewModel"/>
/// state machine inspects the flag and demotes
/// <see cref="AutoUpdateMode.Automatic"/> to the NotifyOnly flow for
/// this adapter (banner with Install button rather than silent
/// download-and-restart).</para>
/// </summary>
public sealed class BrowserNotifyUpdateService : IUpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/geevensingh/DiffViewer/releases";
    private const string GitHubReleasesPageUrl = "https://github.com/geevensingh/DiffViewer/releases/latest";

    private readonly HttpClient _http;
    private readonly Version _currentVersion;
    private readonly bool _includePreReleases;
    private readonly Action<string> _openUrl;

    public BrowserNotifyUpdateService(
        HttpClient http,
        Version currentVersion,
        bool includePreReleases,
        Action<string>? openUrl = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
        _includePreReleases = includePreReleases;
        _openUrl = openUrl ?? OpenUrlInDefaultBrowser;
    }

    public bool CanAutoApply => false;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl + "?per_page=10");
            // GitHub API requires a User-Agent; rejects the request
            // with 403 otherwise. The version string makes our
            // requests identifiable in GitHub's logs.
            req.Headers.UserAgent.ParseAdd($"DiffViewer/{_currentVersion}");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return UpdateCheckResult.NoUpdateAvailable;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var releases = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(
                stream,
                SerializerOptions,
                ct).ConfigureAwait(false);
            if (releases is null) return UpdateCheckResult.NoUpdateAvailable;

            // Pick the highest-version non-draft release, respecting
            // the user's include-prereleases preference. Drafts are
            // never user-visible. We compare by System.Version which
            // ignores the SemVer pre-release suffix entirely — so
            // v1.5.0-rc1 and v1.5.0 parse as the same Version (1.5.0);
            // acceptable for Phase 5 because users who opt into
            // pre-releases AND are mid-cycle between rc1 and rc2 is a
            // genuine edge case. Improve to SemVer-aware comparison
            // if it ever bites.
            var newest = releases
                .Where(r => !r.Draft)
                .Where(r => _includePreReleases || !r.Prerelease)
                .Select(r => new { Release = r, Version = TryParseVersion(r.TagName) })
                .Where(x => x.Version is not null)
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();

            if (newest is null) return UpdateCheckResult.NoUpdateAvailable;
            if (newest.Version! <= _currentVersion) return UpdateCheckResult.NoUpdateAvailable;

            return new UpdateCheckResult
            {
                IsAvailable = true,
                Version = newest.Version!.ToString(3),
                // OpaqueHandle carries the per-release page URL so
                // ApplyOnNextLaunchAsync can launch the user directly
                // at the new release rather than a generic /latest
                // page. Falls back to /releases/latest if the API
                // didn't return a html_url for some reason.
                OpaqueHandle = string.IsNullOrEmpty(newest.Release.HtmlUrl)
                    ? GitHubReleasesPageUrl
                    : newest.Release.HtmlUrl,
            };
        }
        catch (Exception)
        {
            // Network down, rate-limited, JSON shape changed, etc.
            // Periodic re-check will try again at the next tick.
            return UpdateCheckResult.NoUpdateAvailable;
        }
    }

    public Task DownloadAsync(UpdateCheckResult update, CancellationToken ct) =>
        Task.CompletedTask;

    public Task ApplyOnNextLaunchAsync(UpdateCheckResult update, CancellationToken ct)
    {
        if (!update.IsAvailable) return Task.CompletedTask;
        var url = update.OpaqueHandle as string;
        if (string.IsNullOrEmpty(url)) url = GitHubReleasesPageUrl;
        try { _openUrl(url); }
        catch (Exception) { /* best-effort */ }
        return Task.CompletedTask;
    }

    private static void OpenUrlInDefaultBrowser(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });
    }

    internal static Version? TryParseVersion(string? tagName)
    {
        if (string.IsNullOrEmpty(tagName)) return null;
        var trimmed = tagName.TrimStart('v', 'V');
        // Strip SemVer pre-release / build-metadata suffixes; System.Version
        // doesn't understand them and would reject the parse.
        var hyphen = trimmed.IndexOf('-');
        if (hyphen > 0) trimmed = trimmed[..hyphen];
        var plus = trimmed.IndexOf('+');
        if (plus > 0) trimmed = trimmed[..plus];
        return Version.TryParse(trimmed, out var v) ? v : null;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease);
}
