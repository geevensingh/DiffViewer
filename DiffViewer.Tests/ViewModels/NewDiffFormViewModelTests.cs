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

    // === WorkingTreeVsHeadFormViewModel ===

    [Fact]
    public void WorkingTreeVsHead_EmptyPath_IsNotValid_NoError()
    {
        var v = new FakeValidator();
        var form = new WorkingTreeVsHeadFormViewModel(v);

        form.IsValid.Should().BeFalse("required field is empty");
        form.ValidationError.Should().BeNull("empty field shows no error message");
    }

    [Fact]
    public void WorkingTreeVsHead_ValidPath_IsValid()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");

        var form = new WorkingTreeVsHeadFormViewModel(v) { RepoPath = @"C:\repo" };

        form.IsValid.Should().BeTrue();
        form.ValidationError.Should().BeNull();
    }

    [Fact]
    public void WorkingTreeVsHead_InvalidPath_HasError()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\nope"] = new RepoPathValidation.Invalid("not a repo");

        var form = new WorkingTreeVsHeadFormViewModel(v) { RepoPath = @"C:\nope" };

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
        var form = new WorkingTreeVsHeadFormViewModel(v) { RepoPath = @"C:\repo" };

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

        var form = new WorkingTreeVsHeadFormViewModel(v, prefilledRepoPath: @"C:\already");

        form.RepoPath.Should().Be(@"C:\already");
        form.IsValid.Should().BeTrue("pre-fill should pass validation on construction");
    }

    // === WorkingTreeVsCommitFormViewModel ===

    [Fact]
    public void WorkingTreeVsCommit_RequiresBothFields()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");

        var form = new WorkingTreeVsCommitFormViewModel(v) { RepoPath = @"C:\repo" };
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

        var form = new WorkingTreeVsCommitFormViewModel(v) { RepoPath = @"C:\repo", CommitIsh = "main" };

        form.IsValid.Should().BeTrue();
        var source = form.BuildLaunchSource();
        var local = source.Should().BeOfType<DiffLaunchSource.Local>().Subject;
        local.Parsed.Left.Should().BeOfType<DiffSide.CommitIsh>().Which.Reference.Should().Be("main");
        local.Parsed.Right.Should().BeOfType<DiffSide.WorkingTree>();
    }

    // === CommitVsCommitFormViewModel ===

    [Fact]
    public void CommitVsCommit_RequiresThreeFields()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        v.CommitResults[(@"C:\repo", "main")] = new CommitIshValidation.Valid();

        var form = new CommitVsCommitFormViewModel(v)
        {
            RepoPath = @"C:\repo",
            BaseCommit = "main",
        };

        form.IsValid.Should().BeFalse("compare-commit is empty");
    }

    [Fact]
    public void CommitVsCommit_BaseInvalid_SurfaceErrorBeforeReachingCompare()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        v.CommitResults[(@"C:\repo", "bogus")] = new CommitIshValidation.Invalid("Cannot resolve `bogus`.");
        v.CommitResults[(@"C:\repo", "feature")] = new CommitIshValidation.Valid();

        var form = new CommitVsCommitFormViewModel(v)
        {
            RepoPath = @"C:\repo",
            BaseCommit = "bogus",
            CompareCommit = "feature",
        };

        form.IsValid.Should().BeFalse();
        form.ValidationError.Should().Contain("bogus");
    }

    [Fact]
    public void CommitVsCommit_AllValid_BuildsLocalWithBothSidesAsCommitIsh()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        v.CommitResults[(@"C:\repo", "main")] = new CommitIshValidation.Valid();
        v.CommitResults[(@"C:\repo", "feature")] = new CommitIshValidation.Valid();

        var form = new CommitVsCommitFormViewModel(v)
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

    // === GitHubPullRequestFormViewModel ===

    [Fact]
    public void GitHubPr_EmptyUrl_IsNotValid_NoError()
    {
        var v = new FakeValidator();
        var form = new GitHubPullRequestFormViewModel(v);
        form.IsValid.Should().BeFalse();
        form.ValidationError.Should().BeNull();
    }

    [Fact]
    public void GitHubPr_ValidUrl_BuildsGitHubPullRequestSource()
    {
        var v = new FakeValidator();
        var pr = new PullRequestRef("github.com", "octocat", "hello-world", 17);
        v.PrResults["https://github.com/octocat/hello-world/pull/17"] = new PullRequestUrlValidation.Valid(pr);

        var form = new GitHubPullRequestFormViewModel(v)
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

        var form = new GitHubPullRequestFormViewModel(v) { PullRequestUrl = "garbage" };

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
        var form = new GitHubPullRequestFormViewModel(v);

        Action act = () => form.BuildLaunchSource();
        act.Should().Throw<InvalidOperationException>();
    }
}
