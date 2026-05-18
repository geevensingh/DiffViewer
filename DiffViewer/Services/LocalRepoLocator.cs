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
/// promise that. For the recents tier, MRU order is the tiebreaker so
/// the most-recently-used checkout wins.</para>
/// </remarks>
public sealed class LocalRepoLocator : ILocalRepoLocator, IDisposable
{
    /// <summary>Default per-root scan timeout. Slow UNC mounts must not block.</summary>
    public static readonly TimeSpan DefaultPerRootTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly ISettingsService _settings;
    private readonly IRecentContextsService _recents;
    private readonly IRepoInspector _inspector;
    private readonly TimeSpan _perRootTimeout;
    private readonly IRootScanRunner _scanRunner;
    private readonly object _gate = new();

    // Roots-tier cache: keyed by AppSettings.RepoRoots, invalidated on
    // ISettingsService.Changed when the roots list actually differs.
    private Dictionary<RepoUrlKey, string>? _rootsCache;
    private IReadOnlyList<string> _cachedRootsSnapshot = Array.Empty<string>();

    // Recents-tier cache: keyed by the deduped MRU set of recent
    // CanonicalRepoPath values, invalidated on
    // IRecentContextsService.Changed when that set actually differs.
    private Dictionary<RepoUrlKey, string>? _recentsCache;
    private IReadOnlyList<string> _cachedRecentsPathsSnapshot = Array.Empty<string>();

    public LocalRepoLocator(
        ISettingsService settings,
        IRepoInspector inspector,
        IRecentContextsService recents,
        TimeSpan? perRootTimeout = null)
        : this(settings, inspector, recents, perRootTimeout, scanRunner: null)
    {
    }

    internal LocalRepoLocator(
        ISettingsService settings,
        IRepoInspector inspector,
        IRecentContextsService recents,
        TimeSpan? perRootTimeout,
        IRootScanRunner? scanRunner)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        _recents = recents ?? throw new ArgumentNullException(nameof(recents));
        _perRootTimeout = perRootTimeout ?? DefaultPerRootTimeout;
        _scanRunner = scanRunner ?? new TaskRunRootScanRunner();
        _settings.Changed += OnSettingsChanged;
        _recents.Changed += OnRecentsChanged;
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

        // 2. Recent contexts the user has already opened. The currently-
        //    active diff's clone always lives here (it just got recorded
        //    by RecentContextsService.RecordLaunchAsync on launch), so
        //    PR-launching back into "the repo I'm staring at" Just Works
        //    without any RepoRoots configured. MRU order is the
        //    tiebreaker when the same (host, owner, repo) key matches
        //    multiple recent paths (e.g. two checkouts of the same
        //    repo): the most recently used wins.
        var recentsCache = GetOrBuildRecentsCache();
        if (recentsCache.TryGetValue(key, out var recentPath))
        {
            return new LocalRepoLookup(recentPath, LocalRepoMatchSource.RecentContext);
        }

        // 3. Cached scan results across all configured roots.
        var rootsCache = GetOrBuildRootsCache(snapshot.RepoRoots);
        if (rootsCache.TryGetValue(key, out var scannedPath))
        {
            return new LocalRepoLookup(scannedPath, LocalRepoMatchSource.RepoRootScan);
        }

        return new LocalRepoLookup(null, LocalRepoMatchSource.NotFound);
    }

    private Dictionary<RepoUrlKey, string> GetOrBuildRootsCache(IReadOnlyList<string> roots)
    {
        lock (_gate)
        {
            if (_rootsCache is not null && PathListsEqual(_cachedRootsSnapshot, roots))
            {
                return _rootsCache;
            }

            var fresh = new Dictionary<RepoUrlKey, string>();
            foreach (var root in roots)
            {
                ScanRootWithTimeout(root, fresh);
            }
            _rootsCache = fresh;
            _cachedRootsSnapshot = roots.ToList();
            return _rootsCache;
        }
    }

    private Dictionary<RepoUrlKey, string> GetOrBuildRecentsCache()
    {
        // Snapshot recents off-lock — IRecentContextsService.Current
        // returns an atomically-replaced immutable list, so a read is
        // consistent even if the service is being mutated concurrently.
        var paths = DedupRecentPaths(_recents.Current);

        lock (_gate)
        {
            if (_recentsCache is not null && PathListsEqual(_cachedRecentsPathsSnapshot, paths))
            {
                return _recentsCache;
            }

            var fresh = new Dictionary<RepoUrlKey, string>();
            foreach (var path in paths)
            {
                ProbeRecentPathWithTimeout(path, fresh);
            }
            _recentsCache = fresh;
            _cachedRecentsPathsSnapshot = paths;
            return _recentsCache;
        }
    }

    private static IReadOnlyList<string> DedupRecentPaths(IReadOnlyList<RecentLaunchContext> recents)
    {
        // Recents may contain the same path with different (Left, Right)
        // sides. We only want to probe each distinct path once, but we
        // must preserve MRU order so the first-match-wins tiebreaker
        // hits the most-recently-used path for a given key.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(recents.Count);
        foreach (var entry in recents)
        {
            var path = entry.Identity.CanonicalRepoPath;
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (seen.Add(path)) result.Add(path);
        }
        return result;
    }

    private void ProbeRecentPathWithTimeout(string path, Dictionary<RepoUrlKey, string> sink)
    {
        // Reuse the per-root timeout / scan-runner machinery so a
        // recent path that points at a slow UNC mount or a vanished
        // drive can't block PR-mode either. The runner key uses the
        // path itself; tests can swap in a fake IRootScanRunner.
        try
        {
            if (!_scanRunner.TryRunWithTimeout(
                    path, () => ProbeRecentPath(path), _perRootTimeout, out var hits))
            {
                return;
            }
            foreach (var (key, hitPath) in hits)
            {
                // First match (in MRU order) wins for a given key.
                sink.TryAdd(key, hitPath);
            }
        }
        catch
        {
            // Same defensive bag-of-errors guard as ScanRootWithTimeout:
            // one slow / broken recent path must not poison the cache.
        }
    }

    private IReadOnlyList<(RepoUrlKey Key, string Path)> ProbeRecentPath(string path)
    {
        var hits = new List<(RepoUrlKey, string)>();
        if (!_inspector.IsRepository(path)) return hits;

        var remotes = _inspector.GetRemoteUrls(path);
        var seenKeys = new HashSet<RepoUrlKey>();
        foreach (var remoteUrl in remotes)
        {
            if (RemoteUrlMatcher.TryExtractKey(remoteUrl) is { } key
                && seenKeys.Add(key))
            {
                hits.Add((key, path));
            }
        }
        return hits;
    }

    private void ScanRootWithTimeout(string root, Dictionary<RepoUrlKey, string> sink)
    {
        if (string.IsNullOrWhiteSpace(root)) return;

        try
        {
            if (!_scanRunner.TryRunWithTimeout(
                    root, () => ScanRoot(root), _perRootTimeout, out var hits))
            {
                // Slow root (slow UNC, root of C:\, network outage) —
                // skip it. We'll try again on the next cache rebuild
                // when settings change.
                return;
            }
            foreach (var (key, path) in hits)
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

    private static bool PathListsEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
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
        // Repo roots are the only field that affects the roots cache.
        // The mappings dictionary is consulted on every TryLocate call
        // (not cached) so it doesn't need invalidation here.
        if (!PathListsEqual(e.Previous.RepoRoots, e.Current.RepoRoots))
        {
            lock (_gate) { _rootsCache = null; }
        }
    }

    private void OnRecentsChanged(object? sender, EventArgs e)
    {
        // Recents events fire on any state change — record, remove, or
        // even just a LastUsedUtc bump from re-launching an existing
        // entry. Only the deduped path set affects the cache; drop only
        // when that set has actually changed so back-to-back launches
        // of the same context don't force a re-probe.
        var newPaths = DedupRecentPaths(_recents.Current);
        lock (_gate)
        {
            if (!PathListsEqual(_cachedRecentsPathsSnapshot, newPaths))
            {
                _recentsCache = null;
            }
        }
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _recents.Changed -= OnRecentsChanged;
    }
}
