using System.Diagnostics.CodeAnalysis;

namespace DiffViewer.Models;

/// <summary>
/// A reference to a GitHub pull request, identified by host, owner, repo,
/// and number. Equality is case-sensitive, so <see cref="TryParse"/>
/// normalizes host/owner/repo to lowercase: GitHub treats them
/// case-insensitively and we want record equality (dedup, dictionary keys)
/// to behave the same way when users copy-paste mixed-case URLs.
/// </summary>
public sealed record PullRequestRef(string Host, string Owner, string Repo, int Number) : IReviewRef
{
    /// <summary>
    /// Stable provider tag persisted in <c>recents.json</c>. The recents
    /// deserializer treats a missing tag as <c>"github"</c> for
    /// back-compat with files written before <see cref="IReviewRef"/>
    /// existed, so changing this string is a forward-incompatible
    /// migration — don't.
    /// </summary>
    public const string GitHubProviderId = "github";

    /// <inheritdoc />
    public string ProviderId => GitHubProviderId;

    /// <inheritdoc />
    public string Slug => $"{Owner}/{Repo}#{Number}";

    /// <inheritdoc />
    public string WebUrl => $"https://{Host}/{Owner}/{Repo}/pull/{Number}";

    /// <inheritdoc />
    public int IdentityNumber => Number;

    /// <summary>
    /// Parses a GitHub pull request URL of the form
    /// <c>https://github.com/{owner}/{repo}/pull/{number}</c>. Accepts
    /// the same URL with trailing path segments (<c>/files</c>,
    /// <c>/commits/&lt;sha&gt;</c>, etc.), query strings, and fragments.
    /// Rejects other GitHub paths and other hosts (v1 = github.com only).
    /// </summary>
    /// <param name="url">The URL string to parse. May be <c>null</c>.</param>
    /// <param name="pr">The parsed reference, or <c>null</c> on failure.</param>
    /// <param name="error">A human-readable reason on failure, or <c>null</c> on success.</param>
    /// <returns><c>true</c> iff parsing succeeded.</returns>
    public static bool TryParse(
        string? url,
        [NotNullWhen(true)] out PullRequestRef? pr,
        [NotNullWhen(false)] out string? error)
    {
        pr = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            error = "URL is empty.";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = $"`{url}` is not a valid absolute URL.";
            return false;
        }

        if (uri.Scheme is not "http" and not "https")
        {
            error = $"Only http(s) URLs are supported, got scheme `{uri.Scheme}`.";
            return false;
        }

        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Only github.com PR URLs are supported in v1, got host `{uri.Host}`.";
            return false;
        }

        // AbsolutePath is URL-decoded by .NET only for some segments; split on '/' is safe
        // because owner/repo cannot contain slashes and we reject anything that doesn't
        // match the {owner}/{repo}/pull/{number} prefix.
        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 4)
        {
            error = $"URL does not look like a PR: `{url}`.";
            return false;
        }

        if (!string.Equals(segments[2], "pull", StringComparison.OrdinalIgnoreCase))
        {
            error = $"URL is not a pull-request URL (segment 3 is `{segments[2]}`, expected `pull`).";
            return false;
        }

        if (!int.TryParse(segments[3], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int number)
            || number <= 0)
        {
            error = $"PR number `{segments[3]}` is not a positive integer.";
            return false;
        }

        string owner = segments[0];
        string repo = segments[1];

        if (owner.Length == 0 || repo.Length == 0)
        {
            error = "Owner or repo segment is empty.";
            return false;
        }

        pr = new PullRequestRef(
            Host: uri.Host.ToLowerInvariant(),
            Owner: owner.ToLowerInvariant(),
            Repo: repo.ToLowerInvariant(),
            Number: number);
        error = null;
        return true;
    }
}
