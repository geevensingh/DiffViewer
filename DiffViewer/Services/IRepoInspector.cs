using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Minimal seam over libgit2 used by <see cref="LocalRepoLocator"/>.
/// Lets tests exercise the locator's caching, root-scan ordering, and
/// timeout logic without touching the file system.
/// </summary>
public interface IRepoInspector
{
    /// <summary>
    /// Cheap check: does <paramref name="path"/> look like a valid git
    /// repository? Production implementation calls
    /// <c>LibGit2Sharp.Repository.IsValid</c> (stats <c>.git/HEAD</c>);
    /// the cheap probe matters because <see cref="LocalRepoLocator"/>
    /// runs it against every immediate child of every configured
    /// <see cref="AppSettings.RepoRoots"/> entry.
    /// </summary>
    bool IsRepository(string path);

    /// <summary>
    /// Enumerate the URLs of every remote configured on the repo at
    /// <paramref name="path"/>. Production implementation walks
    /// <c>Repository.Network.Remotes</c>. Returns an empty list (never
    /// <c>null</c>, never throws) for non-repos or unreadable repos.
    /// </summary>
    IReadOnlyList<string> GetRemoteUrls(string path);
}
