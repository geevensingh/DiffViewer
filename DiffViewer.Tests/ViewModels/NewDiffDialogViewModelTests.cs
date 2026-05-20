using System.Collections.Generic;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

public class NewDiffDialogViewModelTests
{
    private sealed class FakeValidator : IDiffLaunchValidator
    {
        public RepoPathValidation ValidateRepoPath(string raw) =>
            new RepoPathValidation.Valid(raw);
        public CommitIshValidation ValidateCommitIsh(string canonicalRepoPath, string commitIsh) =>
            new CommitIshValidation.Valid();
        public PullRequestUrlValidation ValidatePullRequestUrl(string url) =>
            PullRequestRef.TryParse(url, out var pr, out var error)
                ? new PullRequestUrlValidation.Valid(pr)
                : new PullRequestUrlValidation.Invalid(error);
    }

    private sealed class StubRefEnumerator : IGitRefEnumerator
    {
        public RefEnumerationResult Enumerate(string canonicalRepoPath) => RefEnumerationResult.Empty;
        public string? TryComputeMergeBase(string canonicalRepoPath, string refA, string refB) => null;
        public string? TryGetDefaultRemoteBranch(string canonicalRepoPath) => null;
    }

    private static NewDiffDialogViewModel MakeVm(
        IDiffLaunchValidator? validator = null,
        string? prefilledRepoPath = null,
        string? initialProviderId = null,
        string? seedPullRequestUrl = null)
    {
        var registry = DiffModeRegistry.BuildDefault();
        return new NewDiffDialogViewModel(
            registry,
            validator ?? new FakeValidator(),
            new StubRefEnumerator(),
            new NullRecentContextsService(),
            prefilledRepoPath,
            initialProviderId,
            seedPullRequestUrl);
    }

    [Fact]
    public void OpensOnFirstProvider_ByDefault()
    {
        var vm = MakeVm();
        vm.SelectedProvider.Should().BeSameAs(vm.Providers[0]);
    }

    [Fact]
    public void OpensOnRequestedProvider_WhenInitialProviderIdMatches()
    {
        var vm = MakeVm(initialProviderId: GitHubPullRequestProvider.ProviderId);
        vm.SelectedProvider.Id.Should().Be(GitHubPullRequestProvider.ProviderId);
        vm.CurrentForm.Should().BeOfType<GitHubPullRequestFormViewModel>();
    }

    [Fact]
    public void UnknownInitialProviderId_FallsBackToFirst()
    {
        var vm = MakeVm(initialProviderId: "totally-bogus");
        vm.SelectedProvider.Should().BeSameAs(vm.Providers[0]);
    }

    [Fact]
    public void SeedPullRequestUrl_RoutesIntoPrForm_AndImmediatelyValidates()
    {
        // A1 end-to-end at the dialog-VM level: when the host detects
        // a PR URL on the clipboard, it passes (initialProviderId =
        // github.pr, seedPullRequestUrl = url). The PR form should
        // open pre-filled with OK enabled.
        const string url = "https://github.com/octocat/hello-world/pull/42";
        var vm = MakeVm(
            initialProviderId: GitHubPullRequestProvider.ProviderId,
            seedPullRequestUrl: url);

        vm.SelectedProvider.Id.Should().Be(GitHubPullRequestProvider.ProviderId);
        var form = vm.CurrentForm.Should().BeOfType<GitHubPullRequestFormViewModel>().Subject;
        form.PullRequestUrl.Should().Be(url);
        vm.IsOkEnabled.Should().BeTrue();
        vm.OkCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void SeedPullRequestUrl_DoesNotAffectLocalForms()
    {
        // Local-mode forms ignore the PR seed entirely — they read
        // PrefilledRepoPath, not SeedPullRequestUrl. Sanity: opening
        // on the default first provider with a seed URL set still
        // produces an empty repo-path form.
        var vm = MakeVm(seedPullRequestUrl: "https://github.com/octocat/hello-world/pull/42");

        vm.SelectedProvider.Id.Should().Be(WorkingTreeVsHeadProvider.ProviderId);
        vm.CurrentForm.Should().BeOfType<WorkingTreeVsHeadFormViewModel>();
        vm.IsOkEnabled.Should().BeFalse("repo path is still empty");
    }

    [Fact]
    public void Ok_DisabledOnEmptyForm()
    {
        var vm = MakeVm();
        // The first provider is "working-tree-vs-head" with no inputs:
        // empty repo path → not valid → OK disabled.
        vm.IsOkEnabled.Should().BeFalse();
        vm.OkCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Ok_EnabledOnceFormBecomesValid()
    {
        var vm = MakeVm(prefilledRepoPath: @"C:\repo");
        // The first provider gets the pre-fill; FakeValidator marks any
        // path as Valid; so the form is valid out of the gate.
        vm.CurrentForm.Should().BeOfType<WorkingTreeVsHeadFormViewModel>();
        vm.IsOkEnabled.Should().BeTrue();
    }

    [Fact]
    public void SwitchingProvider_PreservesFormStateOnReSelect()
    {
        var vm = MakeVm();

        var first = vm.CurrentForm;
        vm.SelectedProvider = vm.Providers[1]; // working-tree-vs-commit
        var second = vm.CurrentForm;
        second.Should().NotBeSameAs(first);

        vm.SelectedProvider = vm.Providers[0]; // back to first
        vm.CurrentForm.Should().BeSameAs(first, "form cache preserves partial input across mode switches");
    }

    [Fact]
    public void FormValidationChange_RaisesIsOkEnabled()
    {
        var vm = MakeVm();
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        ((WorkingTreeVsHeadFormViewModel)vm.CurrentForm).RepoPath = @"C:\new";

        changes.Should().Contain(nameof(NewDiffDialogViewModel.IsOkEnabled));
    }

    [Fact]
    public async Task Ok_ResolvesCompletion_WithLaunchSource()
    {
        var vm = MakeVm(prefilledRepoPath: @"C:\repo");

        vm.OkCommand.Execute(null);

        var result = await vm.Completion;
        result.Should().NotBeNull();
        result.Should().BeOfType<DiffLaunchSource.Local>();
    }

    [Fact]
    public async Task Cancel_ResolvesCompletion_WithNull()
    {
        var vm = MakeVm();

        vm.CancelCommand.Execute(null);

        var result = await vm.Completion;
        result.Should().BeNull();
    }

    [Fact]
    public async Task ForceCancel_ResolvesCompletion_WithNull()
    {
        // Simulates the [X] window-button path: dialog code-behind
        // notifies the VM, which short-circuits to a cancelled result.
        var vm = MakeVm();

        vm.ForceCancel();

        var result = await vm.Completion;
        result.Should().BeNull();
    }

    [Fact]
    public async Task ForceCancel_AfterOk_IsIdempotent()
    {
        // ForceCancel runs from the dialog's Closed handler; OK already
        // closed the dialog so the second resolve must not overwrite
        // the first result. (Both TrySetResult calls; second is a no-op.)
        var vm = MakeVm(prefilledRepoPath: @"C:\repo");

        vm.OkCommand.Execute(null);
        vm.ForceCancel();

        var result = await vm.Completion;
        result.Should().NotBeNull("OK already resolved with a launch source");
    }
}
