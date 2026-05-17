using System.Text.RegularExpressions;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Parse a git remote URL into a canonical <see cref="RepoUrlKey"/>. Used
/// by <see cref="LocalRepoLocator"/> to compare every remote on a candidate
/// clone against the PR's (host, owner, repo) triple, regardless of
/// whether the user added the remote in HTTPS or SSH form.
/// </summary>
/// <remarks>
/// Supported forms:
/// <list type="bullet">
///   <item><c>https://{host}/owner/repo</c> (with optional trailing
///         <c>.git</c> and trailing <c>/</c>; <c>http://</c> also accepted
///         for completeness).</item>
///   <item><c>git@{host}:owner/repo[.git]</c> — the standard
///         <c>scp</c>-style SSH form Git installs default to.</item>
///   <item><c>ssh://[user@]{host}[:port]/owner/repo[.git]</c> — the
///         less-common but RFC-correct SSH long form.</item>
/// </list>
/// Anything else (file://, named remotes pointing at local paths,
/// malformed URLs) parses to <c>null</c> — the locator simply ignores
/// that remote.
/// </remarks>
internal static class RemoteUrlMatcher
{
    // Anchored at both ends so a trailing path component (e.g., a
    // submodule's deeper path) can't sneak past the parser.
    private static readonly Regex HttpsForm = new(
        @"^https?://([^/]+)/([^/]+)/([^/]+?)(?:\.git)?/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SshShortForm = new(
        @"^git@([^:]+):([^/]+)/([^/]+?)(?:\.git)?/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SshLongForm = new(
        @"^ssh://(?:[^@]+@)?([^:/]+)(?::\d+)?/([^/]+)/([^/]+?)(?:\.git)?/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Try to extract a canonical <see cref="RepoUrlKey"/> from a git
    /// remote URL. Returns <c>null</c> when the URL is empty or doesn't
    /// match any supported form.
    /// </summary>
    public static RepoUrlKey? TryExtractKey(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl)) return null;
        var trimmed = remoteUrl.Trim();

        Match m;
        // Order matters: SshLongForm starts with ssh://, SshShortForm
        // starts with git@, HttpsForm starts with http(s)://. They're
        // mutually exclusive, so the order is just for clarity.
        if ((m = HttpsForm.Match(trimmed)).Success ||
            (m = SshLongForm.Match(trimmed)).Success ||
            (m = SshShortForm.Match(trimmed)).Success)
        {
            return RepoUrlKey.From(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
        }

        return null;
    }
}
