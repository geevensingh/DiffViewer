using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

public class ViewStashFormViewModelTests
{
    [Fact]
    public void EmptyRepoPath_IsNotValid_NoError()
    {
        var form = CreateForm(repoValid: true);

        form.IsValid.Should().BeFalse("required fields empty");
        form.ValidationError.Should().BeNull("empty fields don't surface errors");
    }

    [Fact]
    public async Task ValidRepoWithStashes_SelectStash_IsValid()
    {
        var stashes = new[]
        {
            new StashEntry(0, "stash@{0}", "WIP on master", DateTimeOffset.UtcNow, "aaa0000", "aaa0000"),
        };
        var form = CreateForm(repoValid: true, repoPath: @"C:\repo", stashes: stashes);
        form.RepoPath = @"C:\repo";
        await form.EnumerateStashesAsync();

        form.Stashes.Should().HaveCount(1);
        form.SelectedStash = stashes[0];

        form.IsValid.Should().BeTrue();
        form.ValidationError.Should().BeNull();
    }

    [Fact]
    public async Task ValidRepoWithNoStashes_ShowsNoStashesError()
    {
        var form = CreateForm(repoValid: true, repoPath: @"C:\repo", stashes: Array.Empty<StashEntry>());
        form.RepoPath = @"C:\repo";
        await form.EnumerateStashesAsync();

        form.IsValid.Should().BeFalse();
        form.ValidationError.Should().Be("No stashes in this repository.");
    }

    [Fact]
    public void InvalidRepoPath_ShowsRepoError()
    {
        var form = CreateForm(repoValid: false, repoPath: @"C:\nope");
        form.RepoPath = @"C:\nope";

        form.IsValid.Should().BeFalse();
        form.ValidationError.Should().Contain("not a repo");
    }

    [Fact]
    public async Task BuildLaunchSource_EmitsStashAndParent()
    {
        var stashes = new[]
        {
            new StashEntry(0, "stash@{0}", "WIP", DateTimeOffset.UtcNow, "aaa0000", "aaa0000"),
            new StashEntry(1, "stash@{1}", "older", DateTimeOffset.UtcNow, "bbb0000", "bbb0000"),
        };
        var form = CreateForm(repoValid: true, repoPath: @"C:\repo", stashes: stashes);
        form.RepoPath = @"C:\repo";
        await form.EnumerateStashesAsync();
        form.SelectedStash = stashes[1];

        var source = form.BuildLaunchSource();

        source.Should().BeOfType<DiffLaunchSource.Local>();
        var local = (DiffLaunchSource.Local)source;
        local.Parsed.Left.Should().Be(new DiffSide.CommitIsh("stash@{1}^1"));
        local.Parsed.Right.Should().Be(new DiffSide.CommitIsh("stash@{1}"));
        local.Parsed.RepoPath.Should().Be(@"C:\repo");
    }

    [Fact]
    public void BuildLaunchSource_WithoutSelection_Throws()
    {
        var form = CreateForm(repoValid: true, repoPath: @"C:\repo");
        form.RepoPath = @"C:\repo";

        var act = () => form.BuildLaunchSource();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task ChangingRepoPath_ClearsSelection()
    {
        var stashes = new[]
        {
            new StashEntry(0, "stash@{0}", "WIP", DateTimeOffset.UtcNow, "aaa0000", "aaa0000"),
        };
        var form = CreateForm(repoValid: true, repoPath: @"C:\repo", stashes: stashes);
        form.RepoPath = @"C:\repo";
        await form.EnumerateStashesAsync();
        form.SelectedStash = stashes[0];
        form.IsValid.Should().BeTrue();

        // Change repo → selection should be cleared (stashes may
        // re-enumerate asynchronously, but the pick is always reset).
        form.RepoPath = @"C:\other";

        form.SelectedStash.Should().BeNull();
    }

    // ---- helpers -------------------------------------------------------

    private static ViewStashFormViewModel CreateForm(
        bool repoValid = true,
        string repoPath = "",
        IReadOnlyList<StashEntry>? stashes = null)
    {
        var validator = new FakeValidator(repoValid);
        var enumerator = new FakeStashEnumerator(stashes ?? Array.Empty<StashEntry>());
        var deps = new FormDependencies(validator, enumerator, new NullRecentContextsService(), repoPath);
        return new ViewStashFormViewModel(deps, enumerateRunner: work => Task.FromResult(work()));
    }

    private sealed class FakeValidator : IDiffLaunchValidator
    {
        private readonly bool _repoValid;

        public FakeValidator(bool repoValid) => _repoValid = repoValid;

        public RepoPathValidation ValidateRepoPath(string raw) =>
            _repoValid && !string.IsNullOrWhiteSpace(raw)
                ? new RepoPathValidation.Valid(raw)
                : new RepoPathValidation.Invalid("not a repo");

        public CommitIshValidation ValidateCommitIsh(string canonicalRepoPath, string commitIsh) =>
            new CommitIshValidation.Valid();

        public PullRequestUrlValidation ValidatePullRequestUrl(string url) =>
            new PullRequestUrlValidation.Invalid("not supported");
    }

    private sealed class FakeStashEnumerator : IGitRefEnumerator
    {
        private readonly IReadOnlyList<StashEntry> _stashes;

        public FakeStashEnumerator(IReadOnlyList<StashEntry> stashes) => _stashes = stashes;

        public RefEnumerationResult Enumerate(string canonicalRepoPath) =>
            new RefEnumerationResult(
                Array.Empty<RefEntry>(),
                Array.Empty<RefEntry>(),
                Array.Empty<RefEntry>(),
                _stashes);

        public string? TryComputeMergeBase(string canonicalRepoPath, string refA, string refB) => null;
        public string? TryGetDefaultRemoteBranch(string canonicalRepoPath) => null;
    }
}
