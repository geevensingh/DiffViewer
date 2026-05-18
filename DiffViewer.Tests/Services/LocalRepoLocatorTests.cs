using System.Collections.Concurrent;
using System.IO;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

public sealed class LocalRepoLocatorTests : IDisposable
{
    private readonly string _tempRoot;

    public LocalRepoLocatorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DiffViewer.LocatorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best-effort */ }
    }

    private string CreateScannableDir(string name)
    {
        var path = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; private set; } = new();
        public SettingsLoadOutcome LastLoadOutcome => SettingsLoadOutcome.Loaded;
        public event EventHandler<SettingsChangedEventArgs>? Changed;

        public void Save(AppSettings updated)
        {
            var previous = Current;
            Current = updated;
            Changed?.Invoke(this, new SettingsChangedEventArgs(previous, updated));
        }

        public AppSettings Update(Func<AppSettings, AppSettings> mutate)
        {
            Save(mutate(Current));
            return Current;
        }
    }

    private sealed class FakeRepoInspector : IRepoInspector
    {
        public Dictionary<string, IReadOnlyList<string>> RemotesByPath { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentBag<string> IsRepositoryCalls { get; } = new();
        public ConcurrentBag<string> GetRemoteUrlsCalls { get; } = new();
        public Func<string, bool>? OverrideIsRepository { get; set; }

        public bool IsRepository(string path)
        {
            IsRepositoryCalls.Add(path);
            if (OverrideIsRepository is not null) return OverrideIsRepository(path);
            return RemotesByPath.ContainsKey(path);
        }

        public IReadOnlyList<string> GetRemoteUrls(string path)
        {
            GetRemoteUrlsCalls.Add(path);
            return RemotesByPath.TryGetValue(path, out var remotes) ? remotes : Array.Empty<string>();
        }
    }

    /// <summary>
    /// Minimal <see cref="IRecentContextsService"/> fake. Only the
    /// <see cref="Current"/> snapshot and <see cref="Changed"/> event
    /// are exercised by <see cref="LocalRepoLocator"/>; the mutation
    /// methods are no-ops and tests manipulate state via
    /// <see cref="ReplaceWith"/>.
    /// </summary>
    private sealed class FakeRecentContextsService : IRecentContextsService
    {
        private IReadOnlyList<RecentLaunchContext> _current = Array.Empty<RecentLaunchContext>();
        public IReadOnlyList<RecentLaunchContext> Current => _current;
        public event EventHandler? Changed;

        public Task RecordLaunchAsync(ContextIdentity identity, DiffSide leftDisplay, DiffSide rightDisplay, IReviewRef? review = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RemoveAsync(ContextIdentity identity, CancellationToken ct = default)
            => Task.CompletedTask;

        public void ReplaceWith(params string[] canonicalPaths)
        {
            _current = canonicalPaths
                .Select(p => new RecentLaunchContext(
                    new ContextIdentity(p, new DiffSide.WorkingTree(), new DiffSide.CommitIsh("HEAD")),
                    new DiffSide.WorkingTree(),
                    new DiffSide.CommitIsh("HEAD"),
                    DateTimeOffset.UtcNow))
                .ToList();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    [Fact]
    public void TryLocate_ExplicitMappingHit_TakesPrecedenceOverScan()
    {
        var settings = new FakeSettingsService();
        var key = RepoUrlKey.From("github.com", "owner", "repo");
        var rootRepo = CreateScannableDir("scanned-repo");
        var explicitPath = CreateScannableDir("explicit-clone");

        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[rootRepo] = new[] { "https://github.com/owner/repo.git" };

        settings.Save(settings.Current with
        {
            RepoRoots = new[] { _tempRoot },
            RepoUrlMappings = new Dictionary<RepoUrlKey, string> { [key] = explicitPath },
        });

        using var locator = new LocalRepoLocator(settings, inspector, new FakeRecentContextsService());
        var result = locator.TryLocate("github.com", "owner", "repo");

        result.Source.Should().Be(LocalRepoMatchSource.ExplicitMapping);
        result.Path.Should().Be(explicitPath);

        // No scan needed when a mapping hit: the inspector should never
        // have been asked about the root contents.
        inspector.IsRepositoryCalls.Should().BeEmpty();
    }

    [Fact]
    public void TryLocate_NoMappingNoScanMatch_ReturnsNotFound()
    {
        var settings = new FakeSettingsService();
        var inspector = new FakeRepoInspector();
        settings.Save(settings.Current with { RepoRoots = new[] { _tempRoot } });

        using var locator = new LocalRepoLocator(settings, inspector, new FakeRecentContextsService());
        var result = locator.TryLocate("github.com", "owner", "repo");

        result.Source.Should().Be(LocalRepoMatchSource.NotFound);
        result.Path.Should().BeNull();
    }

    [Fact]
    public void TryLocate_ScanFindsHttpsRemote_Returns()
    {
        var settings = new FakeSettingsService();
        var repoDir = CreateScannableDir("my-clone");
        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[repoDir] = new[] { "https://github.com/owner/repo.git" };

        settings.Save(settings.Current with { RepoRoots = new[] { _tempRoot } });

        using var locator = new LocalRepoLocator(settings, inspector, new FakeRecentContextsService());
        var result = locator.TryLocate("github.com", "owner", "repo");

        result.Source.Should().Be(LocalRepoMatchSource.RepoRootScan);
        result.Path.Should().Be(repoDir);
    }

    [Fact]
    public void TryLocate_MatchIsCaseInsensitive()
    {
        var settings = new FakeSettingsService();
        var repoDir = CreateScannableDir("my-clone");
        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[repoDir] = new[] { "https://github.com/Owner/Repo.git" };

        settings.Save(settings.Current with { RepoRoots = new[] { _tempRoot } });

        using var locator = new LocalRepoLocator(settings, inspector, new FakeRecentContextsService());

        // PR URL had different casing — RepoUrlKey.From lowercases on
        // both sides so it still matches.
        var result = locator.TryLocate("GITHUB.COM", "OWNER", "REPO");
        result.Path.Should().Be(repoDir);
    }

    [Fact]
    public void TryLocate_IteratesAllRemotes_NotJustOrigin()
    {
        // OSS fork-of-upstream workflow: clone's `origin` points at the
        // user's fork; clone's `upstream` points at the canonical PR
        // target. We must find the clone via `upstream`.
        var settings = new FakeSettingsService();
        var repoDir = CreateScannableDir("forked-clone");
        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[repoDir] = new[]
        {
            "https://github.com/me/forked-repo.git",         // origin
            "https://github.com/upstream-owner/repo.git",    // upstream
        };

        settings.Save(settings.Current with { RepoRoots = new[] { _tempRoot } });

        using var locator = new LocalRepoLocator(settings, inspector, new FakeRecentContextsService());
        var result = locator.TryLocate("github.com", "upstream-owner", "repo");

        result.Path.Should().Be(repoDir);
    }

    [Fact]
    public void TryLocate_NonRepoChildren_AreIgnored()
    {
        var settings = new FakeSettingsService();
        CreateScannableDir("not-a-repo-1");
        var repoDir = CreateScannableDir("real-repo");
        CreateScannableDir("not-a-repo-2");
        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[repoDir] = new[] { "git@github.com:owner/repo.git" };

        settings.Save(settings.Current with { RepoRoots = new[] { _tempRoot } });

        using var locator = new LocalRepoLocator(settings, inspector, new FakeRecentContextsService());
        var result = locator.TryLocate("github.com", "owner", "repo");
        result.Path.Should().Be(repoDir);
    }

    [Fact]
    public void TryLocate_InspectorThrows_OneRootSurvivesAnother()
    {
        var settings = new FakeSettingsService();
        var good = CreateScannableDir("good-repo");
        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[good] = new[] { "https://github.com/owner/repo.git" };

        // Add a fake "root" path that doesn't exist — Directory.EnumerateDirectories
        // will throw / return nothing. The locator must skip it cleanly.
        var bogus = Path.Combine(_tempRoot, "does-not-exist-" + Guid.NewGuid().ToString("N"));

        settings.Save(settings.Current with { RepoRoots = new[] { bogus, _tempRoot } });

        using var locator = new LocalRepoLocator(settings, inspector, new FakeRecentContextsService());
        var result = locator.TryLocate("github.com", "owner", "repo");

        result.Path.Should().Be(good);
    }

    [Fact]
    public void TryLocate_CachesScanResults_AcrossCalls()
    {
        var settings = new FakeSettingsService();
        var repoDir = CreateScannableDir("cached-repo");
        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[repoDir] = new[] { "https://github.com/owner/repo.git" };

        settings.Save(settings.Current with { RepoRoots = new[] { _tempRoot } });

        using var locator = new LocalRepoLocator(settings, inspector, new FakeRecentContextsService());

        locator.TryLocate("github.com", "owner", "repo");
        locator.TryLocate("github.com", "owner", "repo");
        locator.TryLocate("github.com", "missing", "missing"); // miss, cache still consulted

        // The directory was enumerated once. Subsequent lookups hit the cache.
        inspector.IsRepositoryCalls.Count(p => p == repoDir).Should().Be(1);
    }

    [Fact]
    public void SettingsChanged_RepoRootsModified_InvalidatesCache()
    {
        var settings = new FakeSettingsService();
        var firstRoot = CreateScannableDir("root-1");
        var repoInFirst = CreateScannableDir(@"root-1\repo");
        var secondRoot = CreateScannableDir("root-2");
        var repoInSecond = CreateScannableDir(@"root-2\repo");

        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[repoInFirst] = new[] { "https://github.com/owner/repo.git" };
        inspector.RemotesByPath[repoInSecond] = new[] { "https://github.com/owner/repo.git" };

        settings.Save(settings.Current with { RepoRoots = new[] { firstRoot } });
        using var locator = new LocalRepoLocator(settings, inspector, new FakeRecentContextsService());

        locator.TryLocate("github.com", "owner", "repo").Path.Should().Be(repoInFirst);

        // Switch the root: must drop the cache and re-scan.
        settings.Save(settings.Current with { RepoRoots = new[] { secondRoot } });

        locator.TryLocate("github.com", "owner", "repo").Path.Should().Be(repoInSecond);
    }

    [Fact]
    public void SettingsChanged_OnlyMappingsModified_DoesNotForceRescan()
    {
        // Mappings live outside the scan cache — adding one shouldn't
        // cost a re-scan.
        var settings = new FakeSettingsService();
        var repoDir = CreateScannableDir("cached-repo");
        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[repoDir] = new[] { "https://github.com/owner/repo.git" };

        settings.Save(settings.Current with { RepoRoots = new[] { _tempRoot } });
        using var locator = new LocalRepoLocator(settings, inspector, new FakeRecentContextsService());

        locator.TryLocate("github.com", "owner", "repo");
        var callsAfterFirst = inspector.IsRepositoryCalls.Count(p => p == repoDir);

        settings.Save(settings.Current with
        {
            RepoUrlMappings = new Dictionary<RepoUrlKey, string>
            {
                [RepoUrlKey.From("github.com", "unrelated", "repo")] = @"C:\elsewhere",
            },
        });

        locator.TryLocate("github.com", "owner", "repo");
        inspector.IsRepositoryCalls.Count(p => p == repoDir).Should().Be(callsAfterFirst);
    }

    [Fact]
    public void TryLocate_MultipleRoots_FirstMatchWins()
    {
        var settings = new FakeSettingsService();
        var root1 = CreateScannableDir("r1");
        var repo1 = CreateScannableDir(@"r1\clone");
        var root2 = CreateScannableDir("r2");
        var repo2 = CreateScannableDir(@"r2\clone");

        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[repo1] = new[] { "https://github.com/owner/repo.git" };
        inspector.RemotesByPath[repo2] = new[] { "https://github.com/owner/repo.git" };

        settings.Save(settings.Current with { RepoRoots = new[] { root1, root2 } });

        using var locator = new LocalRepoLocator(settings, inspector, new FakeRecentContextsService());
        locator.TryLocate("github.com", "owner", "repo").Path.Should().Be(repo1);

        // Swap the order: now repo2 should be returned.
        settings.Save(settings.Current with { RepoRoots = new[] { root2, root1 } });
        locator.TryLocate("github.com", "owner", "repo").Path.Should().Be(repo2);
    }

    [Fact]
    public void TryLocate_SlowRoot_TimesOut_OtherRootsStillScanned()
    {
        // Deterministic test: inject a fake IRootScanRunner that marks
        // one root as "timed out" by name, never invoking its scan
        // delegate. Other roots are scanned inline on the calling
        // thread. This decouples the test from the .NET thread pool's
        // ability to schedule two Task.Run calls within the per-root
        // timeout — that scheduling latency is exactly what flaked on
        // CI when both the slow root's still-running scan and the
        // fast root's pending Task.Run competed for workers.
        var settings = new FakeSettingsService();
        var slowRoot = CreateScannableDir("slow-root");
        var fastRoot = CreateScannableDir("fast-root");
        var fastChild = CreateScannableDir(@"fast-root\fast-clone");

        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[fastChild] = new[] { "https://github.com/owner/repo.git" };

        var scanRunner = new SelectiveTimeoutScanRunner(timeoutForRoot: slowRoot);

        settings.Save(settings.Current with { RepoRoots = new[] { slowRoot, fastRoot } });

        using var locator = new LocalRepoLocator(
            settings,
            inspector,
            new FakeRecentContextsService(),
            perRootTimeout: TimeSpan.FromMilliseconds(200),
            scanRunner: scanRunner);
        var result = locator.TryLocate("github.com", "owner", "repo");

        result.Path.Should().Be(fastChild);
        scanRunner.TimedOutRoots.Should().ContainSingle().Which.Should().Be(slowRoot);
        scanRunner.CompletedRoots.Should().ContainSingle().Which.Should().Be(fastRoot);
    }

    [Fact]
    public void TryLocate_RecentContextMatches_ReturnsRecentContextSource()
    {
        // The user is currently looking at a clone whose parent dir is
        // NOT a configured repo root — this is the scenario the user
        // reported: jotjson under c:\repos with c:\repos not in
        // RepoRoots. The recents tier picks it up regardless.
        var settings = new FakeSettingsService();
        var clonePath = CreateScannableDir("active-clone");
        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[clonePath] = new[] { "https://github.com/owner/repo.git" };

        var recents = new FakeRecentContextsService();
        recents.ReplaceWith(clonePath);

        // Crucially, no RepoRoots configured.
        using var locator = new LocalRepoLocator(settings, inspector, recents);
        var result = locator.TryLocate("github.com", "owner", "repo");

        result.Source.Should().Be(LocalRepoMatchSource.RecentContext);
        result.Path.Should().Be(clonePath);
    }

    [Fact]
    public void TryLocate_ExplicitMapping_TakesPrecedenceOverRecentContext()
    {
        var settings = new FakeSettingsService();
        var recentPath = CreateScannableDir("recent-clone");
        var explicitPath = CreateScannableDir("pinned-clone");
        var key = RepoUrlKey.From("github.com", "owner", "repo");

        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[recentPath] = new[] { "https://github.com/owner/repo.git" };
        // explicitPath does not need to be a real repo: explicit
        // mappings short-circuit before any inspector probe.

        settings.Save(settings.Current with
        {
            RepoUrlMappings = new Dictionary<RepoUrlKey, string> { [key] = explicitPath },
        });

        var recents = new FakeRecentContextsService();
        recents.ReplaceWith(recentPath);

        using var locator = new LocalRepoLocator(settings, inspector, recents);
        var result = locator.TryLocate("github.com", "owner", "repo");

        result.Source.Should().Be(LocalRepoMatchSource.ExplicitMapping);
        result.Path.Should().Be(explicitPath);

        // Mapping hit must not have probed recents (or roots).
        inspector.IsRepositoryCalls.Should().BeEmpty();
    }

    [Fact]
    public void TryLocate_RecentContext_TakesPrecedenceOverRepoRootScan()
    {
        // Both tiers know about a clone for the same (host, owner, repo),
        // but at different on-disk paths (e.g. two checkouts). Recents
        // wins because the user just used it — that's the
        // most-relevant signal.
        var settings = new FakeSettingsService();
        var recentPath = CreateScannableDir("recent-clone");
        var scannedRoot = CreateScannableDir("scanned-root");
        var scannedRepo = CreateScannableDir(@"scanned-root\scanned-clone");

        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[recentPath] = new[] { "https://github.com/owner/repo.git" };
        inspector.RemotesByPath[scannedRepo] = new[] { "https://github.com/owner/repo.git" };

        settings.Save(settings.Current with { RepoRoots = new[] { scannedRoot } });

        var recents = new FakeRecentContextsService();
        recents.ReplaceWith(recentPath);

        using var locator = new LocalRepoLocator(settings, inspector, recents);
        var result = locator.TryLocate("github.com", "owner", "repo");

        result.Source.Should().Be(LocalRepoMatchSource.RecentContext);
        result.Path.Should().Be(recentPath);
    }

    [Fact]
    public void TryLocate_RecentContextChanged_InvalidatesRecentsCache()
    {
        var settings = new FakeSettingsService();
        var firstClone = CreateScannableDir("first-clone");
        var secondClone = CreateScannableDir("second-clone");

        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[firstClone] = new[] { "https://github.com/owner/repo.git" };
        inspector.RemotesByPath[secondClone] = new[] { "https://github.com/owner/repo.git" };

        var recents = new FakeRecentContextsService();
        recents.ReplaceWith(firstClone);

        using var locator = new LocalRepoLocator(settings, inspector, recents);
        locator.TryLocate("github.com", "owner", "repo").Path.Should().Be(firstClone);

        // The user closes that clone and opens another. The locator
        // must re-probe and return the new path.
        recents.ReplaceWith(secondClone);
        locator.TryLocate("github.com", "owner", "repo").Path.Should().Be(secondClone);
    }

    [Fact]
    public void TryLocate_RecentContextSamePathTwice_ProbedOnce()
    {
        // Recents can legitimately contain the same path with different
        // (Left, Right) sides (e.g. WT-vs-HEAD and WT-vs-main both live
        // in c:\repos\jotjson). The locator must dedup so the inspector
        // is only probed once per distinct path.
        var settings = new FakeSettingsService();
        var clone = CreateScannableDir("dup-clone");

        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[clone] = new[] { "https://github.com/owner/repo.git" };

        var recents = new FakeRecentContextsService();
        recents.ReplaceWith(clone, clone);

        using var locator = new LocalRepoLocator(settings, inspector, recents);
        var result = locator.TryLocate("github.com", "owner", "repo");

        result.Path.Should().Be(clone);
        inspector.IsRepositoryCalls.Count(p => p == clone).Should().Be(1);
    }

    [Fact]
    public void TryLocate_RecentContextMRUOrder_FirstWinsForDuplicateKey()
    {
        // Two checkouts of the same repo on disk, both in recents. The
        // most-recently-used (index 0) wins because it's the
        // freshest signal of "which clone the user means right now."
        var settings = new FakeSettingsService();
        var newer = CreateScannableDir("newer-checkout");
        var older = CreateScannableDir("older-checkout");

        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[newer] = new[] { "https://github.com/owner/repo.git" };
        inspector.RemotesByPath[older] = new[] { "https://github.com/owner/repo.git" };

        var recents = new FakeRecentContextsService();
        recents.ReplaceWith(newer, older);

        using var locator = new LocalRepoLocator(settings, inspector, recents);
        locator.TryLocate("github.com", "owner", "repo").Path.Should().Be(newer);
    }

    [Fact]
    public void TryLocate_RecentContextNotARepo_FallsThroughToRootsScan()
    {
        // A recents entry that's no longer a valid git repo (rmdir'd,
        // moved, .git folder corrupted) must not block the roots scan
        // from finding a real match.
        var settings = new FakeSettingsService();
        var deadRecent = CreateScannableDir("dead-recent");
        var rootRepo = CreateScannableDir("root-repo");

        var inspector = new FakeRepoInspector();
        // deadRecent is NOT in RemotesByPath → IsRepository returns false.
        inspector.RemotesByPath[rootRepo] = new[] { "https://github.com/owner/repo.git" };

        settings.Save(settings.Current with { RepoRoots = new[] { _tempRoot } });

        var recents = new FakeRecentContextsService();
        recents.ReplaceWith(deadRecent);

        using var locator = new LocalRepoLocator(settings, inspector, recents);
        var result = locator.TryLocate("github.com", "owner", "repo");

        result.Source.Should().Be(LocalRepoMatchSource.RepoRootScan);
        result.Path.Should().Be(rootRepo);
    }

    [Fact]
    public void TryLocate_RecentContextChange_SamePathSet_DoesNotForceRescan()
    {
        // Recents fires Changed on every LastUsedUtc bump, including
        // when re-launching an entry that's already at index 0. The
        // deduped path set didn't change, so we must not re-probe.
        var settings = new FakeSettingsService();
        var clone = CreateScannableDir("stable-clone");

        var inspector = new FakeRepoInspector();
        inspector.RemotesByPath[clone] = new[] { "https://github.com/owner/repo.git" };

        var recents = new FakeRecentContextsService();
        recents.ReplaceWith(clone);

        using var locator = new LocalRepoLocator(settings, inspector, recents);
        locator.TryLocate("github.com", "owner", "repo");
        var probesAfterFirst = inspector.IsRepositoryCalls.Count(p => p == clone);

        // Same path set, just a new Changed event (e.g. LastUsedUtc bump).
        recents.ReplaceWith(clone);
        locator.TryLocate("github.com", "owner", "repo");

        inspector.IsRepositoryCalls.Count(p => p == clone).Should().Be(probesAfterFirst);
    }

    [Fact]
    public void TryLocate_RecentContextSlowPath_TimesOutCleanly()
    {
        // A recent path on a vanished UNC mount must not block PR-mode.
        // The fake scan runner reports the path as timed out without
        // invoking the probe delegate, so we fall through to root scan.
        var settings = new FakeSettingsService();
        var slowRecent = CreateScannableDir("slow-recent");
        var rootRepo = CreateScannableDir("root-repo");

        var inspector = new FakeRepoInspector();
        // slowRecent's remotes would match if probed, but the timeout
        // prevents that. rootRepo is the fallback.
        inspector.RemotesByPath[slowRecent] = new[] { "https://github.com/owner/repo.git" };
        inspector.RemotesByPath[rootRepo] = new[] { "https://github.com/owner/repo.git" };

        settings.Save(settings.Current with { RepoRoots = new[] { _tempRoot } });

        var recents = new FakeRecentContextsService();
        recents.ReplaceWith(slowRecent);

        var scanRunner = new SelectiveTimeoutScanRunner(timeoutForRoot: slowRecent);

        using var locator = new LocalRepoLocator(
            settings,
            inspector,
            recents,
            perRootTimeout: TimeSpan.FromMilliseconds(200),
            scanRunner: scanRunner);
        var result = locator.TryLocate("github.com", "owner", "repo");

        result.Source.Should().Be(LocalRepoMatchSource.RepoRootScan);
        result.Path.Should().Be(rootRepo);
        scanRunner.TimedOutRoots.Should().Contain(slowRecent);
    }

    /// <summary>
    /// Test fake for <see cref="IRootScanRunner"/>: scans the named
    /// "slow" root are reported as having timed out (without running
    /// the scan delegate); every other root is scanned inline.
    /// </summary>
    private sealed class SelectiveTimeoutScanRunner : IRootScanRunner
    {
        private readonly string _timeoutForRoot;
        public List<string> TimedOutRoots { get; } = new();
        public List<string> CompletedRoots { get; } = new();

        public SelectiveTimeoutScanRunner(string timeoutForRoot)
        {
            _timeoutForRoot = timeoutForRoot;
        }

        public bool TryRunWithTimeout(
            string root,
            Func<IReadOnlyList<(RepoUrlKey Key, string Path)>> scan,
            TimeSpan timeout,
            out IReadOnlyList<(RepoUrlKey Key, string Path)> result)
        {
            if (string.Equals(root, _timeoutForRoot, StringComparison.OrdinalIgnoreCase))
            {
                TimedOutRoots.Add(root);
                result = Array.Empty<(RepoUrlKey, string)>();
                return false;
            }

            CompletedRoots.Add(root);
            result = scan();
            return true;
        }
    }
}
