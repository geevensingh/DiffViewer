namespace DiffViewer.Models;

/// <summary>
/// Discriminator for a code-review reference stored in
/// <see cref="RecentLaunchContext.Review"/>. Today's only implementer is
/// <see cref="PullRequestRef"/> (GitHub PR); future implementers will
/// cover Azure DevOps PRs and other providers. Used by the recents
/// service for dedup, by the dropdown for display, and by recents.json
/// (de)serialization to route each row to the right concrete type.
///
/// <para>The interface is intentionally minimal: a stable provider tag
/// for on-disk dispatch, a slug + URL for UI rendering, and a numeric
/// identity used to dedup recents rows that share the same repo
/// identity but point at different reviews. Concrete refs may carry
/// provider-specific fields (host, owner, org, project, …) — the
/// recents pipeline reaches those only via the provider's own
/// (de)serialization path, never through this interface.</para>
/// </summary>
public interface IReviewRef
{
    /// <summary>
    /// Stable provider tag persisted in <c>recents.json</c>
    /// (e.g. <c>"github"</c>, <c>"ado"</c>). Used as the discriminator
    /// on read so a newer binary can route each row to the right
    /// implementer. Missing tag on read = <c>"github"</c>
    /// (back-compat with v2 schema files written before this interface
    /// existed).
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Human-readable identifier suitable for inline display, sans the
    /// repo-name prefix. Example for GitHub: <c>"owner/repo#42"</c>.
    /// </summary>
    string Slug { get; }

    /// <summary>
    /// URL the user can click through to view the review on the
    /// provider's web UI. Surfaced in the recents tooltip.
    /// </summary>
    string WebUrl { get; }

    /// <summary>
    /// Provider-local identity number used to dedup recents rows.
    /// Combined with <see cref="ProviderId"/> this uniquely identifies
    /// a review within a given repo identity, so two reviews with the
    /// same number from different providers stay distinct rows.
    /// </summary>
    int IdentityNumber { get; }
}
