namespace DiffViewer.Models;

/// <summary>
/// Canonical (host, owner, repo) key used by
/// <see cref="AppSettings.RepoUrlMappings"/> and the PR-mode local-repo
/// locator. Host is always part of the key so GitHub Enterprise Server is
/// a config-only follow-up rather than a schema migration — and so
/// <c>microsoft/foo</c> on <c>github.com</c> and the same path on a GHE
/// host can coexist without colliding.
/// </summary>
/// <remarks>
/// All three fields are normalized to lowercase. GitHub treats host /
/// owner / repo case-insensitively, and we want record equality
/// (dictionary lookup, dedup) to behave the same way. Use
/// <see cref="From(string, string, string)"/> instead of the positional
/// constructor for callers that have unnormalized input.
/// </remarks>
public sealed record RepoUrlKey(string Host, string Owner, string Repo)
{
    /// <summary>
    /// Create a key from already-trusted strings, lowercasing each
    /// segment to match the canonical form. Throws on null / empty.
    /// </summary>
    public static RepoUrlKey From(string host, string owner, string repo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        return new RepoUrlKey(
            host.ToLowerInvariant(),
            owner.ToLowerInvariant(),
            repo.ToLowerInvariant());
    }

    /// <summary>
    /// Convenience: derive the canonical key from a <see cref="PullRequestRef"/>
    /// (whose host/owner/repo fields are already lowercase if it came
    /// from <see cref="PullRequestRef.TryParse"/>).
    /// </summary>
    public static RepoUrlKey From(PullRequestRef pr)
    {
        ArgumentNullException.ThrowIfNull(pr);
        return new RepoUrlKey(pr.Host, pr.Owner, pr.Repo);
    }

    /// <summary>
    /// Encode this key as <c>host|owner|repo</c> for use as a JSON
    /// dictionary key. The <c>|</c> separator is chosen because none of
    /// host / owner / repo can legally contain it.
    /// </summary>
    public string ToWireString() => $"{Host}|{Owner}|{Repo}";

    /// <summary>
    /// Parse a <c>host|owner|repo</c> encoded key as produced by
    /// <see cref="ToWireString"/>. Returns <c>null</c> on malformed input
    /// so a hand-edited settings file with a typo can be tolerated
    /// (drops the broken row) instead of crashing the whole load.
    /// </summary>
    public static RepoUrlKey? TryParseWire(string? wire)
    {
        if (string.IsNullOrWhiteSpace(wire)) return null;
        var parts = wire.Split('|');
        if (parts.Length != 3) return null;
        if (parts.Any(string.IsNullOrWhiteSpace)) return null;
        return new RepoUrlKey(
            parts[0].ToLowerInvariant(),
            parts[1].ToLowerInvariant(),
            parts[2].ToLowerInvariant());
    }
}
