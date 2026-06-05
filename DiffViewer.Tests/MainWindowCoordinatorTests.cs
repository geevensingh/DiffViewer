using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DiffViewer;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.Tests.Services;
using DiffViewer.Utility;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests;

/// <summary>
/// Coordinator-level tests that exercise the swap / dispose / record
/// pipeline against real <see cref="TempRepo"/>-backed MainViewModels.
/// Heavier than unit tests but the only way to catch regressions in the
/// "outgoing-disposed-AFTER-swap" invariant.
/// </summary>
public class MainWindowCoordinatorTests
{
    [Fact]
    public async Task InitialLaunchAsync_ParseFailure_ShowsEmptyContextWithNewDiff()
    {
        var dialog = new FakeDialog();
        int? exitCode = null;
        var services = BuildServices(out _);

        var coordinator = new MainWindowCoordinator(
            services, dialog, default, shutdownAction: c => exitCode = c);

        // Three positional args > grammar limit → parse fails. With no
        // recents the coordinator now shows an empty-state shell with
        // "New diff" guidance instead of shutting down.
        var ok = await coordinator.InitialLaunchAsync(new[] { "C:\\nope1", "C:\\nope2", "C:\\nope3" });

        ok.Should().BeTrue();
        dialog.LastError.Should().BeNull();
        exitCode.Should().BeNull();
        coordinator.Current.Should().BeOfType<EmptyContextViewModel>();
    }

    [Fact]
    public async Task InitialLaunchAsync_ParseFailure_AlsoWritesToStderr()
    {
        // The stderrWriter callback is wired in production from
        // App.OnStartup when AttachConsole succeeds; the coordinator just
        // forwards every cold-launch failure message through it so CLI
        // consumers (git difftool) see the error in their terminal.
        var dialog = new FakeDialog();
        var services = BuildServices(out _);
        var stderr = new List<string>();

        var coordinator = new MainWindowCoordinator(
            services, dialog, default,
            shutdownAction: _ => { },
            stderrWriter: stderr.Add);

        await coordinator.InitialLaunchAsync(
            new[] { "C:\\nope1", "C:\\nope2", "C:\\nope3" });

        // Exact wording is the parser's; we just verify the structured
        // failure made it through to the stderr callback verbatim.
        stderr.Should().ContainSingle()
            .Which.Should().Contain("C:\\nope1");
    }

    [Fact]
    public async Task InitialLaunchAsync_ParseFailure_NoStderrWired_ShowsEmptyContext()
    {
        // When no stderrWriter is supplied (GUI double-click launch),
        // the coordinator still shows the empty-state shell (not a dialog).
        var dialog = new FakeDialog();
        int? exitCode = null;
        var services = BuildServices(out _);

        var coordinator = new MainWindowCoordinator(
            services, dialog, default,
            shutdownAction: c => exitCode = c,
            stderrWriter: null);

        var ok = await coordinator.InitialLaunchAsync(new[] { "--bogus" });

        ok.Should().BeTrue();
        dialog.LastError.Should().BeNull();
        exitCode.Should().BeNull();
        coordinator.Current.Should().BeOfType<EmptyContextViewModel>();
    }

    [Fact]
    public async Task InitialLaunchAsync_StderrWriterThrows_DoesNotDerailEmptyContextFallback()
    {
        // Best-effort contract: a misbehaving stderr writer (e.g. parent
        // console closed between attach and write) must not prevent the
        // empty-state shell from showing.
        var dialog = new FakeDialog();
        int? exitCode = null;
        var services = BuildServices(out _);

        var coordinator = new MainWindowCoordinator(
            services, dialog, default,
            shutdownAction: c => exitCode = c,
            stderrWriter: _ => throw new IOException("simulated console gone"));

        var ok = await coordinator.InitialLaunchAsync(new[] { "--bogus" });

        ok.Should().BeTrue();
        dialog.LastError.Should().BeNull();
        exitCode.Should().BeNull();
        coordinator.Current.Should().BeOfType<EmptyContextViewModel>();
    }

    [Fact]
    public async Task StartFromParsedAsync_Success_SetsCurrentAndRecords()
    {
        using var repo = MakeRepoWithCommit();
        var services = BuildServices(out var recents);
        var dialog = new FakeDialog();
        var coordinator = new MainWindowCoordinator(services, dialog, shutdownAction: _ => { });

        var parsed = ParsedFor(repo);
        var ok = await coordinator.StartFromParsedAsync(parsed);

        ok.Should().BeTrue();
        coordinator.Current.Should().NotBeNull();
        recents.RecordedRepoPaths.Should().ContainSingle().Which.Should().Be(repo.Path);

        await coordinator.DisposeCurrentAsync();
    }

    [Fact]
    public async Task SwitchContextAsync_Success_DisposesOutgoing_AFTER_Swap()
    {
        using var repoA = MakeRepoWithCommit();
        using var repoB = MakeRepoWithCommit();
        var services = BuildServices(out _);
        var dialog = new FakeDialog();
        var coordinator = new MainWindowCoordinator(services, dialog, shutdownAction: _ => { });

        (await coordinator.StartFromParsedAsync(ParsedFor(repoA))).Should().BeTrue();
        var firstScope = coordinator.CurrentScope!;
        var firstVm = coordinator.Current!;

        // Capture the dispose-state of the outgoing scope at the moment
        // CurrentChanged fires for the new VM.
        bool? firstScopeDisposedAtSwap = null;
        coordinator.CurrentChanged += (_, _) =>
        {
            if (!ReferenceEquals(coordinator.CurrentScope, firstScope) && firstScopeDisposedAtSwap is null)
            {
                firstScopeDisposedAtSwap = firstScope.IsDisposed;
            }
        };

        (await coordinator.SwitchContextAsync(ParsedFor(repoB))).Should().BeTrue();

        firstScopeDisposedAtSwap.Should().Be(false,
            "outgoing scope must still be alive when Current transitions to the new VM");
        firstScope.IsDisposed.Should().BeTrue("outgoing scope must be disposed after the switch completes");
        coordinator.Current.Should().NotBeNull().And.NotBeSameAs(firstVm);

        await coordinator.DisposeCurrentAsync();
    }

    [Fact]
    public async Task SwitchContextAsync_BuildFailure_DoesNotSwap_AndOffersRemove()
    {
        using var repoA = MakeRepoWithCommit();
        var services = BuildServices(out var recents);
        var dialog = new FakeDialog { ConfirmRemoveResult = true };
        var coordinator = new MainWindowCoordinator(services, dialog, shutdownAction: _ => { });

        (await coordinator.StartFromParsedAsync(ParsedFor(repoA))).Should().BeTrue();
        var stableScope = coordinator.CurrentScope;
        var stableVm = coordinator.Current;

        // Point at a non-existent path so RepositoryService throws.
        var badParsed = new ParsedCommandLine(
            @"C:\definitely-not-a-real-repo-" + System.Guid.NewGuid().ToString("N"),
            new DiffSide.WorkingTree(),
            new DiffSide.CommitIsh("HEAD"));
        var ok = await coordinator.SwitchContextAsync(badParsed);

        ok.Should().BeFalse();
        coordinator.Current.Should().BeSameAs(stableVm, "build failure must leave current VM untouched");
        coordinator.CurrentScope.Should().BeSameAs(stableScope);
        dialog.ConfirmRemoveCallCount.Should().Be(1);
        recents.RemovedRepoPaths.Should().ContainSingle().Which.Should().Be(badParsed.RepoPath);

        await coordinator.DisposeCurrentAsync();
    }

    [Fact]
    public async Task SwitchContextAsync_BuildFailure_NoRemove_WhenUserDeclines()
    {
        using var repoA = MakeRepoWithCommit();
        var services = BuildServices(out var recents);
        var dialog = new FakeDialog { ConfirmRemoveResult = false };
        var coordinator = new MainWindowCoordinator(services, dialog, shutdownAction: _ => { });

        (await coordinator.StartFromParsedAsync(ParsedFor(repoA))).Should().BeTrue();

        var badParsed = new ParsedCommandLine(
            @"C:\definitely-not-a-real-repo-" + System.Guid.NewGuid().ToString("N"),
            new DiffSide.WorkingTree(),
            new DiffSide.CommitIsh("HEAD"));
        await coordinator.SwitchContextAsync(badParsed);

        recents.RemovedRepoPaths.Should().BeEmpty();

        await coordinator.DisposeCurrentAsync();
    }

    [Fact]
    public async Task InitialLaunchAsync_ParseFailure_WithRecents_FallsBackToEmptyContext()
    {
        var dialog = new FakeDialog();
        int? exitCode = null;
        var recents = new FakeRecents();
        recents.SeededItems.Add(MakeContext(@"C:\repos\foo", "main"));
        var services = new AppServices(
            new SettingsService(), new DiffService(), new ExternalAppLauncher(null), recents,
            new FakePullRequestResolver(), new FakeMissingClonePromptHost(), new FakeNewDiffDialogHost());

        var coordinator = new MainWindowCoordinator(
            services, dialog, default, shutdownAction: c => exitCode = c);

        // Three positional args → parse fails. With recents seeded, the
        // coordinator must NOT shut down — instead it installs an
        // EmptyContextViewModel so the user can pick from the dropdown.
        var ok = await coordinator.InitialLaunchAsync(new[] { "C:\\nope1", "C:\\nope2", "C:\\nope3" });

        ok.Should().BeTrue();
        dialog.LastError.Should().BeNull();
        exitCode.Should().BeNull();
        coordinator.Current.Should().BeOfType<EmptyContextViewModel>();
    }

    [Fact]
    public async Task SwitchToRecentAsync_DelegatesToSwitchContextAsync()
    {
        using var repoA = MakeRepoWithCommit();
        using var repoB = MakeRepoWithCommit();
        var services = BuildServices(out var recents);
        var dialog = new FakeDialog();
        var coordinator = new MainWindowCoordinator(services, dialog, shutdownAction: _ => { });

        (await coordinator.StartFromParsedAsync(ParsedFor(repoA))).Should().BeTrue();
        var firstVm = coordinator.Current;

        var recent = new RecentLaunchContext(
            new ContextIdentity(repoB.Path, new DiffSide.WorkingTree(), new DiffSide.CommitIsh("HEAD")),
            new DiffSide.WorkingTree(),
            new DiffSide.CommitIsh("HEAD"),
            DateTimeOffset.UtcNow);

        (await coordinator.SwitchToRecentAsync(recent)).Should().BeTrue();

        coordinator.Current.Should().NotBeNull().And.NotBeSameAs(firstVm);
        recents.RecordedRepoPaths.Should().HaveCount(2,
            "first StartFromParsedAsync recorded repoA, then SwitchToRecentAsync recorded repoB");

        await coordinator.DisposeCurrentAsync();
    }

    // -- Phase 8: PR-review feature -----------------------------------

    [Fact]
    public async Task InitialLaunchFromPullRequestAsync_Ready_StartsContextAndRecordsWithPullRequest()
    {
        using var repo = MakeRepoWithCommit();
        var services = BuildServices(out var recents, out var prResolver, out _);
        var coordinator = new MainWindowCoordinator(services, new FakeDialog(), shutdownAction: _ => { });

        var pr = new PullRequestRef("github.com", "owner", "repo", 7);
        var parsed = ParsedFor(repo);
        prResolver.Results.Enqueue(new DiffViewer.Services.PullRequestResolution.Ready(parsed, pr));

        var ok = await coordinator.InitialLaunchFromPullRequestAsync(pr);

        ok.Should().BeTrue();
        coordinator.Current.Should().BeOfType<DiffViewer.ViewModels.MainViewModel>();
        recents.RecordedRepoPaths.Should().ContainSingle();
        recents.RecordedReviews.Should().ContainSingle()
            .Which.Should().Be(pr);

        await coordinator.DisposeCurrentAsync();
    }

    [Fact]
    public async Task InitialLaunchFromPullRequestAsync_MissingClone_ResolvedThenReady_StartsContext()
    {
        using var repo = MakeRepoWithCommit();
        var services = BuildServices(out _, out var prResolver, out var prompt);
        var coordinator = new MainWindowCoordinator(services, new FakeDialog(), shutdownAction: _ => { });

        var pr = new PullRequestRef("github.com", "owner", "repo", 7);
        prResolver.Results.Enqueue(new DiffViewer.Services.PullRequestResolution.MissingClone(pr));
        prResolver.Results.Enqueue(new DiffViewer.Services.PullRequestResolution.Ready(ParsedFor(repo), pr));
        prompt.NextResult = new DiffViewer.ViewModels.MissingClonePromptResult.Resolved(repo.Path);

        var ok = await coordinator.InitialLaunchFromPullRequestAsync(pr);

        ok.Should().BeTrue();
        prResolver.CallCount.Should().Be(2, "first call surfaced MissingClone, second call after dialog returned Ready");
        prompt.Calls.Should().ContainSingle();

        await coordinator.DisposeCurrentAsync();
    }

    [Fact]
    public async Task InitialLaunchFromPullRequestAsync_MissingClone_Cancelled_FallsBackOrShutsDown()
    {
        var dialog = new FakeDialog();
        int? exitCode = null;
        var services = BuildServices(out _, out var prResolver, out var prompt);
        var coordinator = new MainWindowCoordinator(services, dialog, shutdownAction: c => exitCode = c);

        var pr = new PullRequestRef("github.com", "owner", "repo", 7);
        prResolver.Results.Enqueue(new DiffViewer.Services.PullRequestResolution.MissingClone(pr));
        prompt.NextResult = new DiffViewer.ViewModels.MissingClonePromptResult.Cancelled();

        var ok = await coordinator.InitialLaunchFromPullRequestAsync(pr);

        // No recents → still shows empty-state shell (not shutdown).
        ok.Should().BeTrue();
        dialog.LastError.Should().BeNull();
        exitCode.Should().BeNull();
        coordinator.Current.Should().BeOfType<EmptyContextViewModel>();
        prResolver.CallCount.Should().Be(1, "we never retry after a cancelled dialog");
    }

    [Fact]
    public async Task InitialLaunchFromPullRequestAsync_Failed_RoutesThroughHandleColdLaunchFailure()
    {
        var dialog = new FakeDialog();
        int? exitCode = null;
        var services = BuildServices(out _, out var prResolver, out _);
        var coordinator = new MainWindowCoordinator(services, dialog, shutdownAction: c => exitCode = c);

        var pr = new PullRequestRef("github.com", "owner", "repo", 7);
        prResolver.Results.Enqueue(new DiffViewer.Services.PullRequestResolution.Failed(pr, "GitHub said no."));

        var ok = await coordinator.InitialLaunchFromPullRequestAsync(pr);

        ok.Should().BeTrue();
        exitCode.Should().BeNull();
        var empty = coordinator.Current.Should().BeOfType<EmptyContextViewModel>().Subject;
        empty.Message.Should().Contain("GitHub said no.");
    }

    [Fact]
    public async Task SwitchToRecentAsync_PrRow_ReResolvesAndSwapsContext()
    {
        using var repoA = MakeRepoWithCommit();
        using var repoB = MakeRepoWithCommit();
        var services = BuildServices(out var recents, out var prResolver, out _);
        var coordinator = new MainWindowCoordinator(services, new FakeDialog(), shutdownAction: _ => { });

        // Start in repoA so there's an outgoing context to swap out.
        (await coordinator.StartFromParsedAsync(ParsedFor(repoA))).Should().BeTrue();
        var firstVm = coordinator.Current;

        // Recent row that points at a PR (re-resolve required per D8).
        var pr = new PullRequestRef("github.com", "owner", "repo", 11);
        var recent = new RecentLaunchContext(
            new ContextIdentity(repoB.Path, new DiffSide.CommitIsh("abc"), new DiffSide.CommitIsh("def")),
            new DiffSide.CommitIsh("abc"),
            new DiffSide.CommitIsh("def"),
            DateTimeOffset.UtcNow,
            pr);

        prResolver.Results.Enqueue(new DiffViewer.Services.PullRequestResolution.Ready(ParsedFor(repoB), pr));

        (await coordinator.SwitchToRecentAsync(recent)).Should().BeTrue();

        coordinator.Current.Should().NotBeNull().And.NotBeSameAs(firstVm);
        prResolver.CallCount.Should().Be(1, "PR-mode rows always re-resolve on click");
        recents.RecordedReviews.Should().Contain(pr,
            "the re-resolved row must be re-stamped with the PR ref so subsequent clicks also re-resolve");

        await coordinator.DisposeCurrentAsync();
    }

    [Fact]
    public async Task SwitchToRecentAsync_PrRow_FailedResolve_LeavesCurrentContextLoaded()
    {
        using var repoA = MakeRepoWithCommit();
        var services = BuildServices(out _, out var prResolver, out _);
        var dialog = new FakeDialog();
        var coordinator = new MainWindowCoordinator(services, dialog, shutdownAction: _ => { });

        (await coordinator.StartFromParsedAsync(ParsedFor(repoA))).Should().BeTrue();
        var firstVm = coordinator.Current;

        var pr = new PullRequestRef("github.com", "owner", "repo", 11);
        var recent = new RecentLaunchContext(
            new ContextIdentity(@"C:\repos\gone", new DiffSide.CommitIsh("abc"), new DiffSide.CommitIsh("def")),
            new DiffSide.CommitIsh("abc"),
            new DiffSide.CommitIsh("def"),
            DateTimeOffset.UtcNow,
            pr);

        prResolver.Results.Enqueue(new DiffViewer.Services.PullRequestResolution.Failed(pr, "Auth broke."));

        (await coordinator.SwitchToRecentAsync(recent)).Should().BeFalse();

        // Current context untouched; the user keeps their existing diff.
        coordinator.Current.Should().BeSameAs(firstVm);
        dialog.LastError.Should().Contain("Auth broke.");

        await coordinator.DisposeCurrentAsync();
    }

    [Fact]
    public async Task SwitchToRecentAsync_PrRow_MissingClone_ShowsErrorAndLeavesCurrent()
    {
        using var repoA = MakeRepoWithCommit();
        var services = BuildServices(out _, out var prResolver, out _);
        var dialog = new FakeDialog();
        var coordinator = new MainWindowCoordinator(services, dialog, shutdownAction: _ => { });

        (await coordinator.StartFromParsedAsync(ParsedFor(repoA))).Should().BeTrue();
        var firstVm = coordinator.Current;

        var pr = new PullRequestRef("github.com", "owner", "repo", 11);
        var recent = new RecentLaunchContext(
            new ContextIdentity(@"C:\repos\gone", new DiffSide.CommitIsh("abc"), new DiffSide.CommitIsh("def")),
            new DiffSide.CommitIsh("abc"),
            new DiffSide.CommitIsh("def"),
            DateTimeOffset.UtcNow,
            pr);

        prResolver.Results.Enqueue(new DiffViewer.Services.PullRequestResolution.MissingClone(pr));

        (await coordinator.SwitchToRecentAsync(recent)).Should().BeFalse();

        coordinator.Current.Should().BeSameAs(firstVm);
        dialog.LastError.Should().NotBeNull();
        dialog.LastError.Should().Contain("owner/repo");

        await coordinator.DisposeCurrentAsync();
    }

    [Fact]
    public async Task SwitchContextAsync_PopulatesSwitchingStatusInFlight_AndClearsOnSuccess()
    {
        using var repo = MakeRepoWithCommit();
        var services = BuildServices(out _);
        var dialog = new FakeDialog();

        MainWindowCoordinator? coordinatorRef = null;
        string? statusInFlight = null;
        bool? isSwitchingInFlight = null;
        var coordinator = new MainWindowCoordinator(
            services, dialog, shutdownAction: _ => { },
            contextFactory: async (p, s, sc, ct) =>
            {
                statusInFlight ??= coordinatorRef!.SwitchingStatus;
                isSwitchingInFlight ??= coordinatorRef!.IsSwitching;
                return await CompositionRoot.BuildContextAsync(p, s, sc, ct);
            });
        coordinatorRef = coordinator;

        (await coordinator.SwitchContextAsync(ParsedFor(repo))).Should().BeTrue();

        isSwitchingInFlight.Should().BeTrue("IsSwitching must hold for the entire switch");
        statusInFlight.Should().NotBeNullOrEmpty(
            "the overlay needs visible text the moment the switch starts");
        statusInFlight.Should().Contain("Loading",
            "the in-flight status describes what the user is waiting on");
        coordinator.SwitchingStatus.Should().BeEmpty(
            "status must be cleared once the switch completes so the next switch starts clean");
        coordinator.IsSwitching.Should().BeFalse();

        await coordinator.DisposeCurrentAsync();
    }

    [Fact]
    public async Task SwitchContextAsync_OnContextBuildFailure_ClearsSwitchingStatus()
    {
        using var repoA = MakeRepoWithCommit();
        var services = BuildServices(out _);
        var dialog = new FakeDialog();

        MainWindowCoordinator? coordinatorRef = null;
        bool factoryHasThrown = false;
        var coordinator = new MainWindowCoordinator(
            services, dialog, shutdownAction: _ => { },
            contextFactory: (p, s, sc, ct) =>
            {
                if (!factoryHasThrown)
                {
                    // First call (StartFromParsedAsync below) succeeds so
                    // there's a real outgoing context. Second call (the
                    // SwitchContextAsync under test) throws.
                    return CompositionRoot.BuildContextAsync(p, s, sc, ct);
                }
                throw new ContextBuildException("simulated build failure");
            });
        coordinatorRef = coordinator;

        (await coordinator.StartFromParsedAsync(ParsedFor(repoA))).Should().BeTrue();
        factoryHasThrown = true;

        var parsed = new ParsedCommandLine(
            @"C:\repos\does-not-matter",
            new DiffSide.WorkingTree(),
            new DiffSide.CommitIsh("HEAD"));
        (await coordinator.SwitchContextAsync(parsed)).Should().BeFalse();

        coordinator.SwitchingStatus.Should().BeEmpty(
            "the finally block must clear status even when the switch fails");
        coordinator.IsSwitching.Should().BeFalse();

        await coordinator.DisposeCurrentAsync();
    }

    [Fact]
    public async Task SwitchToAsync_PullRequest_PlumbsProgressIntoResolverAndClearsStatus()
    {
        using var repoA = MakeRepoWithCommit();
        using var repoB = MakeRepoWithCommit();
        var services = BuildServices(out _, out var prResolver, out _);
        var coordinator = new MainWindowCoordinator(
            services, new FakeDialog(), shutdownAction: _ => { });

        (await coordinator.StartFromParsedAsync(ParsedFor(repoA))).Should().BeTrue();

        var pr = new PullRequestRef("github.com", "owner", "repo", 11);
        prResolver.Results.Enqueue(new DiffViewer.Services.PullRequestResolution.Ready(ParsedFor(repoB), pr));

        var source = new DiffViewer.Models.DiffLaunchSource.GitHubPullRequest(pr);
        (await coordinator.SwitchToAsync(source)).Should().BeTrue();

        prResolver.ProgressReports.Should().NotBeEmpty(
            "the coordinator must pass a non-null IProgress<string> so the resolver can phase-report");
        coordinator.SwitchingStatus.Should().BeEmpty(
            "status must be cleared on completion of a PR switch");
        coordinator.IsSwitching.Should().BeFalse();

        await coordinator.DisposeCurrentAsync();
    }

    private static RecentLaunchContext MakeContext(string repoPath, string commitRef)
    {
        var canonical = ContextIdentityFactory.CanonicalizeRepoPath(repoPath);
        var left = new DiffSide.WorkingTree();
        var right = new DiffSide.CommitIsh(commitRef);
        return new RecentLaunchContext(
            new ContextIdentity(canonical, left, right),
            left, right, DateTimeOffset.UtcNow);
    }

    private static AppServices BuildServices(out FakeRecents recents)
        => BuildServices(out recents, out _, out _);

    private static AppServices BuildServices(
        out FakeRecents recents,
        out FakePullRequestResolver prResolver,
        out FakeMissingClonePromptHost prompt)
    {
        recents = new FakeRecents();
        prResolver = new FakePullRequestResolver();
        prompt = new FakeMissingClonePromptHost();
        return new AppServices(
            new SettingsService(),
            new DiffService(),
            new ExternalAppLauncher(null),
            recents,
            prResolver,
            prompt,
            new FakeNewDiffDialogHost());
    }

    private static TempRepo MakeRepoWithCommit()
    {
        var repo = new TempRepo();
        repo.WriteFile("hello.txt", "hello\n");
        repo.InitialCommit();
        return repo;
    }

    private static ParsedCommandLine ParsedFor(TempRepo repo) =>
        new(repo.Path, new DiffSide.WorkingTree(), new DiffSide.CommitIsh("HEAD"));

    private sealed class FakeDialog : IDialogService
    {
        public string? LastError { get; private set; }
        public int ConfirmRemoveCallCount { get; private set; }
        public bool ConfirmRemoveResult { get; set; } = true;

        public void ShowError(string title, string message) => LastError = message;
        public bool ConfirmRemoveStaleEntry(string repoPath, string error)
        {
            ConfirmRemoveCallCount++;
            return ConfirmRemoveResult;
        }
    }

    private sealed class FakeRecents : IRecentContextsService
    {
        public System.Collections.Generic.List<RecentLaunchContext> SeededItems { get; } = new();
        public System.Collections.Generic.IReadOnlyList<RecentLaunchContext> Current => SeededItems;
        public event System.EventHandler? Changed { add { } remove { } }
        public System.Collections.Generic.List<string> RecordedRepoPaths { get; } = new();
        public System.Collections.Generic.List<DiffViewer.Models.IReviewRef?> RecordedReviews { get; } = new();
        public System.Collections.Generic.List<string> RemovedRepoPaths { get; } = new();

        public Task RecordLaunchAsync(ContextIdentity identity, DiffSide leftDisplay, DiffSide rightDisplay, DiffViewer.Models.IReviewRef? review = null, System.Threading.CancellationToken ct = default)
        {
            RecordedRepoPaths.Add(identity.CanonicalRepoPath);
            RecordedReviews.Add(review);
            return Task.CompletedTask;
        }
        public Task RemoveAsync(ContextIdentity identity, System.Threading.CancellationToken ct = default)
        {
            RemovedRepoPaths.Add(identity.CanonicalRepoPath);
            return Task.CompletedTask;
        }
    }

    internal sealed class FakePullRequestResolver : IPullRequestResolver
    {
        public System.Collections.Generic.List<DiffViewer.Models.PullRequestRef> Calls { get; } = new();
        public System.Collections.Generic.Queue<DiffViewer.Services.PullRequestResolution> Results { get; } = new();
        public System.Collections.Generic.List<string> ProgressReports { get; } = new();
        public int CallCount => Calls.Count;

        public Task<DiffViewer.Services.PullRequestResolution> ResolveAsync(
            DiffViewer.Models.PullRequestRef pr,
            System.IProgress<string>? progress,
            System.Threading.CancellationToken ct)
        {
            Calls.Add(pr);
            if (Results.Count == 0)
            {
                throw new System.InvalidOperationException(
                    "FakePullRequestResolver had no result queued for "
                    + $"{pr.Owner}/{pr.Repo}#{pr.Number}.");
            }
            var result = Results.Dequeue();
            progress?.Report($"fake-progress for {pr.Owner}/{pr.Repo}#{pr.Number}");
            ProgressReports.Add($"fake-progress for {pr.Owner}/{pr.Repo}#{pr.Number}");
            return Task.FromResult(result);
        }
    }

    internal sealed class FakeMissingClonePromptHost : IMissingClonePromptHost
    {
        public System.Collections.Generic.List<DiffViewer.Models.PullRequestRef> Calls { get; } = new();
        public DiffViewer.ViewModels.MissingClonePromptResult NextResult { get; set; }
            = new DiffViewer.ViewModels.MissingClonePromptResult.Cancelled();

        public Task<DiffViewer.ViewModels.MissingClonePromptResult> ShowAsync(
            DiffViewer.Models.PullRequestRef pr, System.Threading.CancellationToken ct = default)
        {
            Calls.Add(pr);
            return Task.FromResult(NextResult);
        }
    }

    internal sealed class FakeNewDiffDialogHost : INewDiffDialogHost
    {
        public DiffViewer.Models.DiffLaunchSource? NextResult { get; set; }
        public int Calls { get; private set; }
        public Task<DiffViewer.Models.DiffLaunchSource?> ShowAsync(
            string? prefilledRepoPath, System.Threading.CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(NextResult);
        }
    }

    // ---- IsStashRef tests ----

    [Theory]
    [InlineData("stash@{0}", true)]
    [InlineData("stash@{0}^1", true)]
    [InlineData("stash@{3}", true)]
    [InlineData("stash@{10}^2", true)]
    [InlineData("HEAD", false)]
    [InlineData("master", false)]
    [InlineData("refs/stash", false)]
    public void IsStashRef_DetectsStashReferences(string reference, bool expected)
    {
        var side = new DiffSide.CommitIsh(reference);
        MainWindowCoordinator.IsStashRef(side).Should().Be(expected);
    }

    [Fact]
    public void IsStashRef_WorkingTree_ReturnsFalse()
    {
        MainWindowCoordinator.IsStashRef(new DiffSide.WorkingTree()).Should().BeFalse();
    }
}
