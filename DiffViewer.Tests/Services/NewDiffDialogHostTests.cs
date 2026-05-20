using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;
using DiffViewer.Services;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.Services;

/// <summary>
/// Exercises the testable seam of <see cref="NewDiffDialogHost"/> —
/// the seed-computation (clipboard / MRU / prefilled-repo precedence)
/// and the session-memory update rule for clipboard-forced provider
/// switches. The dialog-display path is UI-coupled and covered by
/// manual smoke; everything else lives behind <c>ComputeSeed</c> and
/// <c>UpdateLastProviderIdForTests</c>.
/// </summary>
public class NewDiffDialogHostTests
{
    private const string ValidPrUrl = "https://github.com/owner/repo/pull/42";
    private const string SecondaryPrUrl = "https://github.com/owner/repo/pull/99";
    private const string PrUrlWithSuffix = "https://github.com/owner/repo/pull/42/files";

    // ---- ComputeSeed: clipboard PR detection (A1) -----------------------

    [Fact]
    public void ComputeSeed_ValidPrUrlOnClipboard_ForcesPrProviderAndSeedsUrl()
    {
        var clipboard = new FakeClipboard { NextText = ValidPrUrl };
        var recents = new FakeRecents();

        var (repoPath, providerOverride, prUrl) =
            NewDiffDialogHost.ComputeSeed(prefilledRepoPath: null, clipboard, recents);

        providerOverride.Should().Be(GitHubPullRequestProvider.ProviderId);
        prUrl.Should().Be(ValidPrUrl);
        repoPath.Should().BeNull();
    }

    [Fact]
    public void ComputeSeed_PrUrlWithFilesSuffix_StillDetected()
    {
        // PullRequestRef.TryParse tolerates trailing /files, /commits/<sha>,
        // query strings, fragments — covered exhaustively in
        // PullRequestRefTests; this guard just confirms the host
        // doesn't accidentally tighten that contract.
        var clipboard = new FakeClipboard { NextText = PrUrlWithSuffix };
        var recents = new FakeRecents();

        var (_, providerOverride, prUrl) =
            NewDiffDialogHost.ComputeSeed(prefilledRepoPath: null, clipboard, recents);

        providerOverride.Should().Be(GitHubPullRequestProvider.ProviderId);
        prUrl.Should().Be(PrUrlWithSuffix);
    }

    [Fact]
    public void ComputeSeed_JunkOnClipboard_NoProviderOverride()
    {
        var clipboard = new FakeClipboard { NextText = "not a url, not even close" };
        var recents = new FakeRecents();

        var (_, providerOverride, prUrl) =
            NewDiffDialogHost.ComputeSeed(prefilledRepoPath: null, clipboard, recents);

        providerOverride.Should().BeNull();
        prUrl.Should().BeNull();
    }

    [Fact]
    public void ComputeSeed_ClipboardEmpty_NoProviderOverride()
    {
        // TryGetText returns false (e.g. clipboard contains a bitmap
        // or another app owns it). Host must silently no-op.
        var clipboard = new FakeClipboard { NextText = null };
        var recents = new FakeRecents();

        var (_, providerOverride, prUrl) =
            NewDiffDialogHost.ComputeSeed(prefilledRepoPath: null, clipboard, recents);

        providerOverride.Should().BeNull();
        prUrl.Should().BeNull();
    }

    [Fact]
    public void ComputeSeed_NonGitHubUrlOnClipboard_NoProviderOverride()
    {
        var clipboard = new FakeClipboard { NextText = "https://example.com/some/page" };
        var recents = new FakeRecents();

        var (_, providerOverride, prUrl) =
            NewDiffDialogHost.ComputeSeed(prefilledRepoPath: null, clipboard, recents);

        providerOverride.Should().BeNull();
        prUrl.Should().BeNull();
    }

    // ---- ComputeSeed: MRU repo-path fallback (A3) -----------------------

    [Fact]
    public void ComputeSeed_NoPrefilledRepoPath_FallsBackToMostRecentMru()
    {
        var clipboard = new FakeClipboard { NextText = null };
        var recents = new FakeRecents();
        recents.ReplaceWith("C:\\repos\\alpha", "C:\\repos\\beta");

        var (repoPath, _, _) =
            NewDiffDialogHost.ComputeSeed(prefilledRepoPath: null, clipboard, recents);

        repoPath.Should().Be("C:\\repos\\alpha");
    }

    [Fact]
    public void ComputeSeed_NoPrefilledRepoPath_EmptyMru_RepoPathNull()
    {
        var clipboard = new FakeClipboard { NextText = null };
        var recents = new FakeRecents();

        var (repoPath, _, _) =
            NewDiffDialogHost.ComputeSeed(prefilledRepoPath: null, clipboard, recents);

        repoPath.Should().BeNull();
    }

    [Fact]
    public void ComputeSeed_WhitespacePrefilledRepoPath_FallsBackToMru()
    {
        // Caller passing "" or "   " is the same intent as null: no
        // current diff context. Fall back to MRU.
        var clipboard = new FakeClipboard { NextText = null };
        var recents = new FakeRecents();
        recents.ReplaceWith("C:\\repos\\alpha");

        var (repoPath, _, _) =
            NewDiffDialogHost.ComputeSeed(prefilledRepoPath: "   ", clipboard, recents);

        repoPath.Should().Be("C:\\repos\\alpha");
    }

    [Fact]
    public void ComputeSeed_PrefilledRepoPathProvided_BeatsMru()
    {
        var clipboard = new FakeClipboard { NextText = null };
        var recents = new FakeRecents();
        recents.ReplaceWith("C:\\repos\\alpha");

        var (repoPath, _, _) =
            NewDiffDialogHost.ComputeSeed(prefilledRepoPath: "C:\\caller\\repo", clipboard, recents);

        repoPath.Should().Be("C:\\caller\\repo");
    }

    [Fact]
    public void ComputeSeed_PrUrlAndMru_BothFlowThrough()
    {
        // The two seeds are orthogonal: clipboard drives provider +
        // URL seed, MRU drives repo path. Confirm they don't
        // interfere — even though the PR form ignores repo path, the
        // user can still click a local provider after the dialog
        // opens and the repo field is already populated.
        var clipboard = new FakeClipboard { NextText = ValidPrUrl };
        var recents = new FakeRecents();
        recents.ReplaceWith("C:\\repos\\alpha");

        var (repoPath, providerOverride, prUrl) =
            NewDiffDialogHost.ComputeSeed(prefilledRepoPath: null, clipboard, recents);

        repoPath.Should().Be("C:\\repos\\alpha");
        providerOverride.Should().Be(GitHubPullRequestProvider.ProviderId);
        prUrl.Should().Be(ValidPrUrl);
    }

    // ---- UpdateLastProviderId: session-memory rule ----------------------
    //
    // Locked design: clipboard-detected PR mode is "for this open only".
    // It must not poison _lastProviderId. But if the user explicitly
    // switches away from the clipboard-forced provider, their choice IS
    // recorded — that's a positive intent signal.

    [Fact]
    public void UpdateLastProviderId_NoOverride_RecordsUserSelection()
    {
        var host = MakeHost();

        host.UpdateLastProviderIdForTests(
            selectedProviderId: WorkingTreeVsCommitProvider.ProviderId,
            initialProviderIdOverride: null);

        host.LastProviderIdForTests.Should().Be(WorkingTreeVsCommitProvider.ProviderId);
    }

    [Fact]
    public void UpdateLastProviderId_ClipboardOverrideAndUserKeepsPrMode_DoesNotPersistOverride()
    {
        // Clipboard forced PR mode and the user hit OK without
        // changing the mode. _lastProviderId stays at whatever it
        // was (here, null — first open of the session).
        var host = MakeHost();

        host.UpdateLastProviderIdForTests(
            selectedProviderId: GitHubPullRequestProvider.ProviderId,
            initialProviderIdOverride: GitHubPullRequestProvider.ProviderId);

        host.LastProviderIdForTests.Should().BeNull();
    }

    [Fact]
    public void UpdateLastProviderId_ClipboardOverrideButUserSwitchedAway_RecordsUserChoice()
    {
        // User had PR mode forced by clipboard, then clicked over to
        // CommitVsCommit and submitted. That switch is a real choice
        // and is recorded.
        var host = MakeHost();

        host.UpdateLastProviderIdForTests(
            selectedProviderId: CommitVsCommitProvider.ProviderId,
            initialProviderIdOverride: GitHubPullRequestProvider.ProviderId);

        host.LastProviderIdForTests.Should().Be(CommitVsCommitProvider.ProviderId);
    }

    [Fact]
    public void UpdateLastProviderId_ClipboardOverrideAndPriorSessionMemory_PreservesPriorValue()
    {
        // First open: user picked CommitVsCommit. Second open:
        // clipboard forces PR mode, user keeps it. Third open should
        // still revert to CommitVsCommit (the original session
        // memory survives the clipboard interlude).
        var host = MakeHost();
        host.UpdateLastProviderIdForTests(
            selectedProviderId: CommitVsCommitProvider.ProviderId,
            initialProviderIdOverride: null);
        host.LastProviderIdForTests.Should().Be(CommitVsCommitProvider.ProviderId);

        host.UpdateLastProviderIdForTests(
            selectedProviderId: GitHubPullRequestProvider.ProviderId,
            initialProviderIdOverride: GitHubPullRequestProvider.ProviderId);

        host.LastProviderIdForTests.Should().Be(CommitVsCommitProvider.ProviderId);
    }

    [Fact]
    public void UpdateLastProviderId_SelectedProviderNull_PreservesPriorValue()
    {
        // The user closed the dialog without committing — selected
        // provider may surface as null. Don't clobber session memory.
        var host = MakeHost();
        host.UpdateLastProviderIdForTests(
            selectedProviderId: CommitVsCommitProvider.ProviderId,
            initialProviderIdOverride: null);

        host.UpdateLastProviderIdForTests(
            selectedProviderId: null,
            initialProviderIdOverride: null);

        host.LastProviderIdForTests.Should().Be(CommitVsCommitProvider.ProviderId);
    }

    // ---- helpers --------------------------------------------------------

    private static NewDiffDialogHost MakeHost()
    {
        // Only UpdateLastProviderId / LastProviderIdForTests are
        // exercised here; the dependencies stay minimal/no-op stubs.
        return new NewDiffDialogHost(
            DiffModeRegistry.BuildDefault(),
            new NoOpValidator(),
            new NoOpRefEnumerator(),
            new FakeRecents(),
            new FakeClipboard(),
            () => null);
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public string? NextText { get; set; }
        public void SetText(string text) { /* not used */ }
        public bool TryGetText([NotNullWhen(true)] out string? text)
        {
            text = NextText;
            return text is not null;
        }
    }

    private sealed class FakeRecents : IRecentContextsService
    {
        private IReadOnlyList<RecentLaunchContext> _current = Array.Empty<RecentLaunchContext>();
        public IReadOnlyList<RecentLaunchContext> Current => _current;
        public event EventHandler? Changed;

        public Task RecordLaunchAsync(
            ContextIdentity identity, DiffSide leftDisplay, DiffSide rightDisplay,
            IReviewRef? review = null, CancellationToken ct = default)
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

    private sealed class NoOpValidator : IDiffLaunchValidator
    {
        public RepoPathValidation ValidateRepoPath(string raw)
            => new RepoPathValidation.Valid(raw);
        public CommitIshValidation ValidateCommitIsh(string canonicalRepoPath, string commitIsh)
            => new CommitIshValidation.Valid();
        public PullRequestUrlValidation ValidatePullRequestUrl(string url)
            => new PullRequestUrlValidation.Invalid("not used");
    }

    private sealed class NoOpRefEnumerator : IGitRefEnumerator
    {
        public RefEnumerationResult Enumerate(string canonicalRepoPath) => RefEnumerationResult.Empty;
        public string? TryComputeMergeBase(string canonicalRepoPath, string refA, string refB) => null;
        public string? TryGetDefaultRemoteBranch(string canonicalRepoPath) => null;
    }
}
