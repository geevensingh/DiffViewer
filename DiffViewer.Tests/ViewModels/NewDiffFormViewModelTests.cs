using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

/// <summary>
/// Tests for the four per-mode form view-models hosted in the "New
/// diff" dialog. Each form covers: required-fields gate, validation
/// error wiring, and BuildLaunchSource output.
/// </summary>
public class NewDiffFormViewModelTests
{
    /// <summary>
    /// In-memory validator that lets each test pre-program success or
    /// failure for repo-path and commit-ish lookups.
    /// </summary>
    private sealed class FakeValidator : IDiffLaunchValidator
    {
        public Dictionary<string, RepoPathValidation> RepoResults { get; } = new();
        public Dictionary<(string repo, string commit), CommitIshValidation> CommitResults { get; } = new();
        public Dictionary<string, PullRequestUrlValidation> PrResults { get; } = new();

        public RepoPathValidation ValidateRepoPath(string raw) =>
            RepoResults.TryGetValue(raw, out var r) ? r
                : new RepoPathValidation.Invalid($"unstubbed repo path: {raw}");

        public CommitIshValidation ValidateCommitIsh(string canonicalRepoPath, string commitIsh) =>
            CommitResults.TryGetValue((canonicalRepoPath, commitIsh), out var r) ? r
                : new CommitIshValidation.Invalid($"unstubbed commit: {commitIsh}");

        public PullRequestUrlValidation ValidatePullRequestUrl(string url) =>
            PrResults.TryGetValue(url, out var r) ? r
                : new PullRequestUrlValidation.Invalid($"unstubbed url: {url}");
    }

    /// <summary>
    /// No-op ref enumerator used by form-VM tests that don't exercise
    /// the picker's enumeration path — every call returns an empty
    /// result so the picker stays in its zero-state and nothing
    /// validates the repo path against a real LibGit2 repo.
    /// </summary>
    private sealed class StubRefEnumerator : IGitRefEnumerator
    {
        public RefEnumerationResult Enumerate(string canonicalRepoPath) => RefEnumerationResult.Empty;
        public string? TryComputeMergeBase(string canonicalRepoPath, string refA, string refB) => null;
    }

    /// <summary>Build a <see cref="FormDependencies"/> bundle for tests
    /// that only care about the validator.</summary>
    private static FormDependencies Deps(IDiffLaunchValidator validator, string? prefilledRepoPath = null)
        => new(validator, new StubRefEnumerator(), new NullRecentContextsService(), prefilledRepoPath);

    // === WorkingTreeVsHeadFormViewModel ===

    [Fact]
    public void WorkingTreeVsHead_EmptyPath_IsNotValid_NoError()
    {
        var v = new FakeValidator();
        var form = new WorkingTreeVsHeadFormViewModel(Deps(v));

        form.IsValid.Should().BeFalse("required field is empty");
        form.ValidationError.Should().BeNull("empty field shows no error message");
    }

    [Fact]
    public void WorkingTreeVsHead_ValidPath_IsValid()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");

        var form = new WorkingTreeVsHeadFormViewModel(Deps(v)) { RepoPath = @"C:\repo" };

        form.IsValid.Should().BeTrue();
        form.ValidationError.Should().BeNull();
    }

    [Fact]
    public void WorkingTreeVsHead_InvalidPath_HasError()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\nope"] = new RepoPathValidation.Invalid("not a repo");

        var form = new WorkingTreeVsHeadFormViewModel(Deps(v)) { RepoPath = @"C:\nope" };

        form.IsValid.Should().BeFalse();
        form.ValidationError.Should().Be("not a repo");
    }

    [Fact]
    public void WorkingTreeVsHead_BuildLaunchSource_ProducesLocalCommitIshHeadVsWorkingTree()
    {
        // Must match what the CLI produces for argv=[repoPath]:
        // left = CommitIsh("HEAD"), right = WorkingTree.
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        var form = new WorkingTreeVsHeadFormViewModel(Deps(v)) { RepoPath = @"C:\repo" };

        var source = form.BuildLaunchSource();

        var local = source.Should().BeOfType<DiffLaunchSource.Local>().Subject;
        local.Parsed.RepoPath.Should().Be(@"C:\repo");
        local.Parsed.Left.Should().BeOfType<DiffSide.CommitIsh>().Which.Reference.Should().Be("HEAD");
        local.Parsed.Right.Should().BeOfType<DiffSide.WorkingTree>();
    }

    [Fact]
    public void WorkingTreeVsHead_PrefilledRepoPath_PopulatesField()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\already"] = new RepoPathValidation.Valid(@"C:\already");

        var form = new WorkingTreeVsHeadFormViewModel(Deps(v, prefilledRepoPath: @"C:\already"));

        form.RepoPath.Should().Be(@"C:\already");
        form.IsValid.Should().BeTrue("pre-fill should pass validation on construction");
    }

    // === WorkingTreeVsCommitFormViewModel ===

    [Fact]
    public void WorkingTreeVsCommit_RequiresBothFields()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");

        var form = new WorkingTreeVsCommitFormViewModel(Deps(v)) { RepoPath = @"C:\repo" };
        form.IsValid.Should().BeFalse("commit-ish is empty");

        form.CommitIsh = "main";
        // Without a stub for the commit-ish, validator returns "unstubbed".
        form.IsValid.Should().BeFalse();
    }

    [Fact]
    public void WorkingTreeVsCommit_AllValid_BuildsLocalWithCommitIshLeftWorkingTreeRight()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        v.CommitResults[(@"C:\repo", "main")] = new CommitIshValidation.Valid();

        var form = new WorkingTreeVsCommitFormViewModel(Deps(v)) { RepoPath = @"C:\repo", CommitIsh = "main" };

        form.IsValid.Should().BeTrue();
        var source = form.BuildLaunchSource();
        var local = source.Should().BeOfType<DiffLaunchSource.Local>().Subject;
        local.Parsed.Left.Should().BeOfType<DiffSide.CommitIsh>().Which.Reference.Should().Be("main");
        local.Parsed.Right.Should().BeOfType<DiffSide.WorkingTree>();
    }

    [Fact]
    public void WorkingTreeVsCommit_PickerWriteBack_PushesValueIntoCommitIshAndRevalidates()
    {
        // The "Pick…" popup's PickRefCommand writes back to the form
        // via the callback passed into RefPickerViewModel. End-to-end
        // smoke: simulate a selection and verify CommitIsh updates and
        // IsValid re-evaluates against the new value.
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        v.CommitResults[(@"C:\repo", "feature")] = new CommitIshValidation.Valid();
        var form = new WorkingTreeVsCommitFormViewModel(Deps(v)) { RepoPath = @"C:\repo" };

        form.CommitIshPicker.PickRefCommand.Execute("feature");

        form.CommitIsh.Should().Be("feature");
        form.IsValid.Should().BeTrue("picker write-back should re-trigger validation");
    }

    [Fact]
    public void WorkingTreeVsCommit_CommitIshPicker_IsEnabled_OnceRepoPathIsValid()
    {
        // Regression guard for the bug that made the Pick… button dead
        // until the user also typed a commit-ish: the picker reads
        // CanonicalRepoPath off the form, which used to be a side
        // effect of ComputeValidationError. ComputeValidationError
        // bailed early when CommitIsh was empty, so the picker stayed
        // disabled forever. The form now canonicalises on
        // OnRepoPathChanged, independent of the commit-ish field.
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        var form = new WorkingTreeVsCommitFormViewModel(Deps(v)) { RepoPath = @"C:\repo" };

        form.CommitIshPicker.CanonicalRepoPath.Should().Be(@"C:\repo");
        form.CommitIshPicker.IsEnabled.Should().BeTrue(
            "the picker should be reachable as soon as the repo path validates");
        form.CommitIsh.Should().BeEmpty("commit-ish must still be empty for the regression case to hold");
    }

    [Fact]
    public void WorkingTreeVsCommit_CommitIshPicker_StaysDisabled_WhenRepoPathInvalid()
    {
        // An invalid repo path can't drive a meaningful branch
        // enumeration — keep the picker disabled and the canonical
        // path null. (The deferred error message itself is gated by
        // HasRequiredInputs and so doesn't surface here.)
        var v = new FakeValidator();
        v.RepoResults[@"C:\nope"] = new RepoPathValidation.Invalid("not a repo");
        var form = new WorkingTreeVsCommitFormViewModel(Deps(v)) { RepoPath = @"C:\nope" };

        form.CommitIshPicker.CanonicalRepoPath.Should().BeNull();
        form.CommitIshPicker.IsEnabled.Should().BeFalse();
    }

    // === CommitVsCommitFormViewModel ===

    [Fact]
    public void CommitVsCommit_RequiresThreeFields()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        v.CommitResults[(@"C:\repo", "main")] = new CommitIshValidation.Valid();

        var form = new CommitVsCommitFormViewModel(Deps(v))
        {
            RepoPath = @"C:\repo",
            BaseCommit = "main",
        };

        form.IsValid.Should().BeFalse("compare-commit is empty");
    }

    [Fact]
    public void CommitVsCommit_BaseInvalid_CompareValid_SurfacesOnlyBaseError()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        v.CommitResults[(@"C:\repo", "bogus")] = new CommitIshValidation.Invalid("Cannot resolve `bogus`.");
        v.CommitResults[(@"C:\repo", "feature")] = new CommitIshValidation.Valid();

        var form = new CommitVsCommitFormViewModel(Deps(v))
        {
            RepoPath = @"C:\repo",
            BaseCommit = "bogus",
            CompareCommit = "feature",
        };

        form.IsValid.Should().BeFalse();
        form.ValidationError.Should().Be("Cannot resolve `bogus`.");
    }

    [Fact]
    public void CommitVsCommit_BothCommitsInvalid_SurfacesBothErrors()
    {
        // Regression guard for the v1.1 UX fix: when both commit-ish
        // fields are invalid (e.g. user typed `main~1` and `main` in a
        // repo whose default branch is `master`), the dialog must
        // surface both errors at once instead of hiding the second
        // failure behind the first.
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        v.CommitResults[(@"C:\repo", "main~1")] = new CommitIshValidation.Invalid("Cannot resolve `main~1`.");
        v.CommitResults[(@"C:\repo", "main")] = new CommitIshValidation.Invalid("Cannot resolve `main`.");

        var form = new CommitVsCommitFormViewModel(Deps(v))
        {
            RepoPath = @"C:\repo",
            BaseCommit = "main~1",
            CompareCommit = "main",
        };

        form.IsValid.Should().BeFalse();
        form.ValidationError.Should().NotBeNull();
        form.ValidationError.Should().Contain("main~1");
        form.ValidationError.Should().Contain("`main`");
        // Both errors on separate lines, base first.
        form.ValidationError.Should().Be("Cannot resolve `main~1`.\nCannot resolve `main`.");
    }

    [Fact]
    public void CommitVsCommit_BaseValid_CompareInvalid_SurfacesOnlyCompareError()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        v.CommitResults[(@"C:\repo", "main")] = new CommitIshValidation.Valid();
        v.CommitResults[(@"C:\repo", "bogus")] = new CommitIshValidation.Invalid("Cannot resolve `bogus`.");

        var form = new CommitVsCommitFormViewModel(Deps(v))
        {
            RepoPath = @"C:\repo",
            BaseCommit = "main",
            CompareCommit = "bogus",
        };

        form.IsValid.Should().BeFalse();
        form.ValidationError.Should().Be("Cannot resolve `bogus`.");
    }

    [Fact]
    public void CommitVsCommit_AllValid_BuildsLocalWithBothSidesAsCommitIsh()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        v.CommitResults[(@"C:\repo", "main")] = new CommitIshValidation.Valid();
        v.CommitResults[(@"C:\repo", "feature")] = new CommitIshValidation.Valid();

        var form = new CommitVsCommitFormViewModel(Deps(v))
        {
            RepoPath = @"C:\repo",
            BaseCommit = "main",
            CompareCommit = "feature",
        };

        form.IsValid.Should().BeTrue();
        var source = form.BuildLaunchSource();
        var local = source.Should().BeOfType<DiffLaunchSource.Local>().Subject;
        local.Parsed.Left.Should().BeOfType<DiffSide.CommitIsh>().Which.Reference.Should().Be("main");
        local.Parsed.Right.Should().BeOfType<DiffSide.CommitIsh>().Which.Reference.Should().Be("feature");
    }

    [Fact]
    public void CommitVsCommit_BaseAndComparePickers_WriteBackIndependently()
    {
        // Each commit-ish input gets its own picker; selecting a ref in
        // the Base picker must NOT also bump Compare (and vice versa).
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        v.CommitResults[(@"C:\repo", "main")] = new CommitIshValidation.Valid();
        v.CommitResults[(@"C:\repo", "feature")] = new CommitIshValidation.Valid();
        var form = new CommitVsCommitFormViewModel(Deps(v)) { RepoPath = @"C:\repo" };

        form.BaseCommitPicker.PickRefCommand.Execute("main");
        form.CompareCommitPicker.PickRefCommand.Execute("feature");

        form.BaseCommit.Should().Be("main");
        form.CompareCommit.Should().Be("feature");
        form.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CommitVsCommit_BothPickers_AreEnabled_OnceRepoPathIsValid()
    {
        // Regression guard mirroring the WorkingTreeVsCommit case:
        // both commit-ish pickers must be reachable as soon as the
        // repo path canonicalises, independent of the commit-ish
        // fields.
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        var form = new CommitVsCommitFormViewModel(Deps(v)) { RepoPath = @"C:\repo" };

        form.BaseCommitPicker.CanonicalRepoPath.Should().Be(@"C:\repo");
        form.BaseCommitPicker.IsEnabled.Should().BeTrue();
        form.CompareCommitPicker.CanonicalRepoPath.Should().Be(@"C:\repo");
        form.CompareCommitPicker.IsEnabled.Should().BeTrue();
        form.BaseCommit.Should().BeEmpty();
        form.CompareCommit.Should().BeEmpty();
    }

    [Fact]
    public void CommitVsCommit_BothPickers_StayDisabled_WhenRepoPathInvalid()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\nope"] = new RepoPathValidation.Invalid("not a repo");
        var form = new CommitVsCommitFormViewModel(Deps(v)) { RepoPath = @"C:\nope" };

        form.BaseCommitPicker.CanonicalRepoPath.Should().BeNull();
        form.BaseCommitPicker.IsEnabled.Should().BeFalse();
        form.CompareCommitPicker.CanonicalRepoPath.Should().BeNull();
        form.CompareCommitPicker.IsEnabled.Should().BeFalse();
    }

    // === GitHubPullRequestFormViewModel ===

    [Fact]
    public void GitHubPr_EmptyUrl_IsNotValid_NoError()
    {
        var v = new FakeValidator();
        var form = new GitHubPullRequestFormViewModel(Deps(v));
        form.IsValid.Should().BeFalse();
        form.ValidationError.Should().BeNull();
    }

    [Fact]
    public void GitHubPr_ValidUrl_BuildsGitHubPullRequestSource()
    {
        var v = new FakeValidator();
        var pr = new PullRequestRef("github.com", "octocat", "hello-world", 17);
        v.PrResults["https://github.com/octocat/hello-world/pull/17"] = new PullRequestUrlValidation.Valid(pr);

        var form = new GitHubPullRequestFormViewModel(Deps(v))
        {
            PullRequestUrl = "https://github.com/octocat/hello-world/pull/17",
        };

        form.IsValid.Should().BeTrue();
        var source = form.BuildLaunchSource();
        source.Should().BeOfType<DiffLaunchSource.GitHubPullRequest>()
            .Which.Pr.Should().Be(pr);
    }

    [Fact]
    public void GitHubPr_InvalidUrl_SurfaceError()
    {
        var v = new FakeValidator();
        v.PrResults["garbage"] = new PullRequestUrlValidation.Invalid("not a URL");

        var form = new GitHubPullRequestFormViewModel(Deps(v)) { PullRequestUrl = "garbage" };

        form.IsValid.Should().BeFalse();
        form.ValidationError.Should().Be("not a URL");
    }

    [Fact]
    public void GitHubPr_BuildLaunchSource_WhenInvalid_Throws()
    {
        // Defensive: the dialog UI gates OK on IsValid, but if a caller
        // forgets that contract, surface a clear exception rather than
        // NullReference.
        var v = new FakeValidator();
        var form = new GitHubPullRequestFormViewModel(Deps(v));

        Action act = () => form.BuildLaunchSource();
        act.Should().Throw<InvalidOperationException>();
    }
}
