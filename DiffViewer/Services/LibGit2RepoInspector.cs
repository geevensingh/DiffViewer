using LibGit2Sharp;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="IRepoInspector"/>: thin wrapper around
/// LibGit2Sharp's <c>Repository.IsValid</c> and
/// <c>Repository.Network.Remotes</c>. Every libgit2 call is wrapped in a
/// catch-all that downgrades exceptions to "not a repo / no remotes"
/// because <see cref="LocalRepoLocator"/> scans whole directory trees
/// where any number of files can be permission-locked, partial clones,
/// or otherwise unhappy.
/// </summary>
internal sealed class LibGit2RepoInspector : IRepoInspector
{
    public bool IsRepository(string path)
    {
        try { return Repository.IsValid(path); }
        catch { return false; }
    }

    public IReadOnlyList<string> GetRemoteUrls(string path)
    {
        try
        {
            using var repo = new Repository(path);
            // Materialize before disposing the Repository handle.
            return repo.Network.Remotes.Select(r => r.Url).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
