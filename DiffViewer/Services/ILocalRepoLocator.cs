using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Find a local clone whose remote URL matches a PR's (host, owner, repo)
/// triple. The PR-launch resolver (Phase 6) consults this before falling
/// back to the missing-clone dialog.
/// </summary>
/// <remarks>
/// <para>Implementations are free-threaded: the result is computed
/// synchronously, but the call may originate from any thread. Production
/// implementations cache the directory scan results per process and
/// invalidate on <see cref="ISettingsService.Changed"/>.</para>
///
/// <para>Lookup order, by design:
/// <list type="number">
///   <item><see cref="AppSettings.RepoUrlMappings"/> (explicit overrides
///         the user picked from the missing-clone dialog).</item>
///   <item><see cref="IRecentContextsService.Current"/> — probe each
///         distinct <see cref="ContextIdentity.CanonicalRepoPath"/> from
///         the MRU list. This is what makes "open a PR for the repo
///         I'm already staring at" Just Work without any
///         <see cref="AppSettings.RepoRoots"/> configured: the active
///         diff's clone is always in recents (recorded by
///         <see cref="IRecentContextsService.RecordLaunchAsync"/> on
///         successful launch). MRU order is preserved on ties, so the
///         most-recently-used checkout wins when multiple recent paths
///         share a (host, owner, repo) key.</item>
///   <item>Scan of each <see cref="AppSettings.RepoRoots"/> entry's
///         immediate children — for each child that is a valid git repo,
///         iterate <b>every</b> configured remote, not just
///         <c>origin</c>. This is what makes the upstream-canonical /
///         origin-fork OSS workflow Just Work — the fork's <c>origin</c>
///         won't match the canonical owner, but its <c>upstream</c>
///         will.</item>
/// </list>
/// </para>
/// </remarks>
public interface ILocalRepoLocator
{
    /// <summary>
    /// Look up the local clone path for <paramref name="host"/>/
    /// <paramref name="owner"/>/<paramref name="repo"/>. Host/owner/repo
    /// comparisons are case-insensitive (see <see cref="RepoUrlKey.From(string, string, string)"/>).
    /// </summary>
    LocalRepoLookup TryLocate(string host, string owner, string repo);
}

/// <summary>
/// Result of an <see cref="ILocalRepoLocator.TryLocate"/> call.
/// <see cref="Path"/> is <c>null</c> iff <see cref="Source"/> is
/// <see cref="LocalRepoMatchSource.NotFound"/>.
/// </summary>
public sealed record LocalRepoLookup(string? Path, LocalRepoMatchSource Source);

/// <summary>How the locator found the path it's returning.</summary>
public enum LocalRepoMatchSource
{
    /// <summary>No mapping and no scanned root matched.</summary>
    NotFound,

    /// <summary>A directory under one of the configured repo roots matched on a remote URL.</summary>
    RepoRootScan,

    /// <summary>An explicit <see cref="AppSettings.RepoUrlMappings"/> entry matched.</summary>
    ExplicitMapping,

    /// <summary>
    /// A path from <see cref="IRecentContextsService.Current"/> probed
    /// positive — the user already opened this clone recently, so we
    /// can resolve the PR against it without configuration.
    /// </summary>
    RecentContext,
}
