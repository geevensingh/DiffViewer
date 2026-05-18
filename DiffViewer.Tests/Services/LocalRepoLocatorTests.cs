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

        using var locator = new LocalRepoLocator(settings, inspector);
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

        using var locator = new LocalRepoLocator(settings, inspector);
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

        using var locator = new LocalRepoLocator(settings, inspector);
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

        using var locator = new LocalRepoLocator(settings, inspector);

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

        using var locator = new LocalRepoLocator(settings, inspector);
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

        using var locator = new LocalRepoLocator(settings, inspector);
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

        using var locator = new LocalRepoLocator(settings, inspector);
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

        using var locator = new LocalRepoLocator(settings, inspector);

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
        using var locator = new LocalRepoLocator(settings, inspector);

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
        using var locator = new LocalRepoLocator(settings, inspector);

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

        using var locator = new LocalRepoLocator(settings, inspector);
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
            perRootTimeout: TimeSpan.FromMilliseconds(200),
            scanRunner: scanRunner);
        var result = locator.TryLocate("github.com", "owner", "repo");

        result.Path.Should().Be(fastChild);
        scanRunner.TimedOutRoots.Should().ContainSingle().Which.Should().Be(slowRoot);
        scanRunner.CompletedRoots.Should().ContainSingle().Which.Should().Be(fastRoot);
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
