using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.ViewModels;
using FluentAssertions;
using Xunit;

namespace DiffViewer.Tests.ViewModels;

/// <summary>
/// Tests for the "Branch vs merge-base" form VM. Mirrors the shape of
/// <see cref="NewDiffFormViewModelTests"/> for the existing three
/// commit-ish forms: required-fields gate, validation wiring,
/// merge-base resolution, and BuildLaunchSource output.
/// </summary>
public class BranchVsMergeBaseFormViewModelTests
{
    private sealed class FakeValidator : IDiffLaunchValidator
    {
        public Dictionary<string, RepoPathValidation> RepoResults { get; } = new();
        public Dictionary<(string repo, string commit), CommitIshValidation> CommitResults { get; } = new();

        public RepoPathValidation ValidateRepoPath(string raw) =>
            RepoResults.TryGetValue(raw, out var r) ? r
                : new RepoPathValidation.Invalid($"unstubbed repo path: {raw}");

        public CommitIshValidation ValidateCommitIsh(string canonicalRepoPath, string commitIsh) =>
            CommitResults.TryGetValue((canonicalRepoPath, commitIsh), out var r) ? r
                : new CommitIshValidation.Invalid($"unstubbed commit: {commitIsh}");

        public PullRequestUrlValidation ValidatePullRequestUrl(string url) =>
            new PullRequestUrlValidation.Invalid("not used in these tests");
    }

    /// <summary>
    /// Programmable enumerator: callers set
    /// <see cref="MergeBaseResults"/> entries for (repo, refA, refB)
    /// triples that should resolve. The default (no entry) returns
    /// null, matching the production "unrelated histories" path.
    /// Ordering does not matter — both directions are recorded.
    /// </summary>
    private sealed class FakeEnumerator : IGitRefEnumerator
    {
        public Dictionary<(string repo, string a, string b), string> MergeBaseResults { get; } = new();

        public RefEnumerationResult Enumerate(string canonicalRepoPath) => RefEnumerationResult.Empty;

        public string? TryComputeMergeBase(string canonicalRepoPath, string refA, string refB)
        {
            if (MergeBaseResults.TryGetValue((canonicalRepoPath, refA, refB), out var sha)) return sha;
            if (MergeBaseResults.TryGetValue((canonicalRepoPath, refB, refA), out sha)) return sha;
            return null;
        }
    }

    private static FormDependencies Deps(
        FakeValidator validator,
        FakeEnumerator enumerator,
        string? prefilledRepoPath = null)
        => new(validator, enumerator, new NullRecentContextsService(), prefilledRepoPath);

    [Fact]
    public void Empty_NotValid_NoError()
    {
        var form = new BranchVsMergeBaseFormViewModel(Deps(new FakeValidator(), new FakeEnumerator()));

        form.IsValid.Should().BeFalse("required fields are empty");
        form.ValidationError.Should().BeNull("empty fields show no error message");
    }

    [Fact]
    public void RequiresThreeFields()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");

        var form = new BranchVsMergeBaseFormViewModel(Deps(v, new FakeEnumerator()))
        {
            RepoPath = @"C:\repo",
            Branch = "feature",
        };

        form.IsValid.Should().BeFalse("merge-base partner is empty");
    }

    [Fact]
    public void AllValid_WithMergeBase_BuildsLocalWithMergeBaseShaLeftBranchRight()
    {
        // Convention: left = merge-base, right = branch tip, so
        // additions land on the right — same shape every other form
        // produces.
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        v.CommitResults[(@"C:\repo", "feature")] = new CommitIshValidation.Valid();
        v.CommitResults[(@"C:\repo", "origin/main")] = new CommitIshValidation.Valid();
        var e = new FakeEnumerator();
        e.MergeBaseResults[(@"C:\repo", "feature", "origin/main")] = "abcdef1234567890";

        var form = new BranchVsMergeBaseFormViewModel(Deps(v, e))
        {
            RepoPath = @"C:\repo",
            Branch = "feature",
            MergeBasePartner = "origin/main",
        };

        form.IsValid.Should().BeTrue();
        var source = form.BuildLaunchSource();
        var local = source.Should().BeOfType<DiffLaunchSource.Local>().Subject;
        local.Parsed.Left.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("abcdef1234567890");
        local.Parsed.Right.Should().BeOfType<DiffSide.CommitIsh>()
            .Which.Reference.Should().Be("feature");
    }

    [Fact]
    public void UnrelatedHistories_SurfacesNoCommonAncestorError()
    {
        // Two refs that don't share a merge-base (FakeEnumerator
        // returns null when (repo, a, b) is unstubbed).
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        v.CommitResults[(@"C:\repo", "alpha")] = new CommitIshValidation.Valid();
        v.CommitResults[(@"C:\repo", "beta")] = new CommitIshValidation.Valid();
        var e = new FakeEnumerator();

        var form = new BranchVsMergeBaseFormViewModel(Deps(v, e))
        {
            RepoPath = @"C:\repo",
            Branch = "alpha",
            MergeBasePartner = "beta",
        };

        form.IsValid.Should().BeFalse();
        form.ValidationError.Should().Contain("No common ancestor");
        form.ValidationError.Should().Contain("alpha");
        form.ValidationError.Should().Contain("beta");
    }

    [Fact]
    public void UnresolvableBranch_PrefersCommitIshErrorOverMergeBaseLookup()
    {
        // If either ref doesn't resolve, surface that more-diagnostic
        // error rather than the less-informative "no common ancestor"
        // FindMergeBase would produce.
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        v.CommitResults[(@"C:\repo", "main")] = new CommitIshValidation.Valid();
        v.CommitResults[(@"C:\repo", "nope")] = new CommitIshValidation.Invalid("Cannot resolve `nope`.");
        var e = new FakeEnumerator();

        var form = new BranchVsMergeBaseFormViewModel(Deps(v, e))
        {
            RepoPath = @"C:\repo",
            Branch = "nope",
            MergeBasePartner = "main",
        };

        form.IsValid.Should().BeFalse();
        form.ValidationError.Should().Be("Cannot resolve `nope`.");
    }

    [Fact]
    public void InvalidRepoPath_RepoErrorWinsOverMergeBaseLookup()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\nope"] = new RepoPathValidation.Invalid("not a repo");
        var e = new FakeEnumerator();

        var form = new BranchVsMergeBaseFormViewModel(Deps(v, e))
        {
            RepoPath = @"C:\nope",
            Branch = "feature",
            MergeBasePartner = "main",
        };

        form.IsValid.Should().BeFalse();
        form.ValidationError.Should().Be("not a repo");
    }

    [Fact]
    public void BothRefsUnresolvable_SurfacesBothErrors()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        v.CommitResults[(@"C:\repo", "x")] = new CommitIshValidation.Invalid("Cannot resolve `x`.");
        v.CommitResults[(@"C:\repo", "y")] = new CommitIshValidation.Invalid("Cannot resolve `y`.");
        var e = new FakeEnumerator();

        var form = new BranchVsMergeBaseFormViewModel(Deps(v, e))
        {
            RepoPath = @"C:\repo",
            Branch = "x",
            MergeBasePartner = "y",
        };

        form.IsValid.Should().BeFalse();
        form.ValidationError.Should().Be("Cannot resolve `x`.\nCannot resolve `y`.");
    }

    [Fact]
    public void BuildLaunchSource_WhenInvalid_Throws()
    {
        // Defensive: the dialog UI gates OK on IsValid, but if a caller
        // forgets that contract, surface a clear exception rather than
        // build a launch with an empty SHA.
        var form = new BranchVsMergeBaseFormViewModel(Deps(new FakeValidator(), new FakeEnumerator()));

        Action act = () => form.BuildLaunchSource();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BranchPickerWriteBack_PushesIntoBranch()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        var e = new FakeEnumerator();
        var form = new BranchVsMergeBaseFormViewModel(Deps(v, e)) { RepoPath = @"C:\repo" };

        form.BranchPicker.PickRefCommand.Execute("feature");

        form.Branch.Should().Be("feature");
    }

    [Fact]
    public void MergeBasePartnerPickerWriteBack_PushesIntoPartner()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        var e = new FakeEnumerator();
        var form = new BranchVsMergeBaseFormViewModel(Deps(v, e)) { RepoPath = @"C:\repo" };

        form.MergeBasePartnerPicker.PickRefCommand.Execute("origin/main");

        form.MergeBasePartner.Should().Be("origin/main");
    }
}
