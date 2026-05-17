using System.IO;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="ILocalRepoLocator"/>. Caches the directory
/// scan per process and invalidates on
/// <see cref="ISettingsService.Changed"/>.
/// </summary>
/// <remarks>
/// <para>Concurrency: <see cref="TryLocate"/> is safe to call from any
/// thread. Cache rebuilds are serialized by an internal lock; the
/// libgit2 work itself happens on a thread-pool task with a per-root
/// timeout so a slow UNC mount can't block the whole resolve.</para>
///
/// <para>Match semantics: for each candidate repo, iterate <b>every</b>
/// configured remote — not just <c>origin</c>. This is what makes the
/// fork-of-upstream workflow work for OSS reviewers: the fork's
/// <c>origin</c> points at the user's fork, but its <c>upstream</c>
/// points at the canonical repo whose PR URL we're resolving.</para>
///
/// <para>Ordering: when the same (host, owner, repo) key matches in
/// multiple roots, the first match wins (preserving repo-roots order).
/// Within a single root, directory enumeration order is
/// platform-defined — usually alphabetical on Windows, but we don't
/// promise that.</para>
/// </remarks>
public sealed class LocalRepoLocator : ILocalRepoLocator, IDisposable
{
    /// <summary>Default per-root scan timeout. Slow UNC mounts must not block.</summary>
    public static readonly TimeSpan DefaultPerRootTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly ISettingsService _settings;
    private readonly IRepoInspector _inspector;
    private readonly TimeSpan _perRootTimeout;
    private readonly object _gate = new();
    private Dictionary<RepoUrlKey, string>? _cache;
    private IReadOnlyList<string> _cachedRootsSnapshot = Array.Empty<string>();

    public LocalRepoLocator(
        ISettingsService settings,
        IRepoInspector inspector,
        TimeSpan? perRootTimeout = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        _perRootTimeout = perRootTimeout ?? DefaultPerRootTimeout;
        _settings.Changed += OnSettingsChanged;
    }

    public LocalRepoLookup TryLocate(string host, string owner, string repo)
    {
        var key = RepoUrlKey.From(host, owner, repo);
        var snapshot = _settings.Current;

        // 1. Explicit mappings always win — the user pinned this clone
        //    via the missing-clone dialog and we should respect that
        //    even if a root-scan would also produce a match.
        if (snapshot.RepoUrlMappings.TryGetValue(key, out var explicitPath))
        {
            return new LocalRepoLookup(explicitPath, LocalRepoMatchSource.ExplicitMapping);
        }

        // 2. Cached scan results across all configured roots.
        var cache = GetOrBuildCache(snapshot.RepoRoots);
        if (cache.TryGetValue(key, out var scannedPath))
        {
            return new LocalRepoLookup(scannedPath, LocalRepoMatchSource.RepoRootScan);
        }

        return new LocalRepoLookup(null, LocalRepoMatchSource.NotFound);
    }

    private Dictionary<RepoUrlKey, string> GetOrBuildCache(IReadOnlyList<string> roots)
    {
        lock (_gate)
        {
            if (_cache is not null && RootsEqual(_cachedRootsSnapshot, roots))
            {
                return _cache;
            }

            var fresh = new Dictionary<RepoUrlKey, string>();
            foreach (var root in roots)
            {
                ScanRootWithTimeout(root, fresh);
            }
            _cache = fresh;
            _cachedRootsSnapshot = roots.ToList();
            return _cache;
        }
    }

    private void ScanRootWithTimeout(string root, Dictionary<RepoUrlKey, string> sink)
    {
        if (string.IsNullOrWhiteSpace(root)) return;

        try
        {
            var task = Task.Run(() => ScanRoot(root));
            if (!task.Wait(_perRootTimeout))
            {
                // Slow root (slow UNC, root of C:\, network outage) —
                // skip it. We'll try again on the next cache rebuild
                // when settings change.
                return;
            }
            foreach (var (key, path) in task.Result)
            {
                // First match wins across roots, mirroring repo-roots
                // priority order.
                sink.TryAdd(key, path);
            }
        }
        catch
        {
            // Any I/O or libgit2 error → skip this root silently. We
            // don't want a single bad root (renamed network share,
            // permission-locked subtree) to break PR-mode entirely.
        }
    }

    private IReadOnlyList<(RepoUrlKey Key, string Path)> ScanRoot(string root)
    {
        var hits = new List<(RepoUrlKey, string)>();

        IEnumerable<string> children;
        try
        {
            if (!Directory.Exists(root)) return hits;
            children = Directory.EnumerateDirectories(root);
        }
        catch
        {
            return hits;
        }

        foreach (var child in children)
        {
            if (!_inspector.IsRepository(child)) continue;
            var remotes = _inspector.GetRemoteUrls(child);
            var seenKeys = new HashSet<RepoUrlKey>();
            foreach (var remoteUrl in remotes)
            {
                if (RemoteUrlMatcher.TryExtractKey(remoteUrl) is { } key
                    && seenKeys.Add(key))
                {
                    hits.Add((key, child));
                }
            }
        }

        return hits;
    }

    private static bool RootsEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        // Repo roots are the only field that affects the cache. The
        // mappings dictionary is consulted on every TryLocate call
        // (not cached) so it doesn't need invalidation here.
        if (!RootsEqual(e.Previous.RepoRoots, e.Current.RepoRoots))
        {
            lock (_gate) { _cache = null; }
        }
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
    }
}
