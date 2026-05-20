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
        public Dictionary<string, string?> DefaultRemoteBranchByRepo { get; } = new();

        public RefEnumerationResult Enumerate(string canonicalRepoPath) => RefEnumerationResult.Empty;

        public string? TryComputeMergeBase(string canonicalRepoPath, string refA, string refB)
        {
            if (MergeBaseResults.TryGetValue((canonicalRepoPath, refA, refB), out var sha)) return sha;
            if (MergeBaseResults.TryGetValue((canonicalRepoPath, refB, refA), out sha)) return sha;
            return null;
        }

        public string? TryGetDefaultRemoteBranch(string canonicalRepoPath)
        {
            return DefaultRemoteBranchByRepo.TryGetValue(canonicalRepoPath, out var name) ? name : null;
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

    [Fact]
    public void BothPickers_AreEnabled_OnceRepoPathIsValid()
    {
        // Regression guard mirroring the WorkingTreeVsCommit /
        // CommitVsCommit cases: both Branch and MergeBasePartner
        // pickers must be reachable as soon as the repo path
        // canonicalises, independent of the other inputs.
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        var form = new BranchVsMergeBaseFormViewModel(Deps(v, new FakeEnumerator()))
        {
            RepoPath = @"C:\repo",
        };

        form.BranchPicker.CanonicalRepoPath.Should().Be(@"C:\repo");
        form.BranchPicker.IsEnabled.Should().BeTrue();
        form.MergeBasePartnerPicker.CanonicalRepoPath.Should().Be(@"C:\repo");
        form.MergeBasePartnerPicker.IsEnabled.Should().BeTrue();
        form.Branch.Should().BeEmpty();
        form.MergeBasePartner.Should().BeEmpty();
    }

    [Fact]
    public void BothPickers_StayDisabled_WhenRepoPathInvalid()
    {
        var v = new FakeValidator();
        v.RepoResults[@"C:\nope"] = new RepoPathValidation.Invalid("not a repo");
        var form = new BranchVsMergeBaseFormViewModel(Deps(v, new FakeEnumerator()))
        {
            RepoPath = @"C:\nope",
        };

        form.BranchPicker.CanonicalRepoPath.Should().BeNull();
        form.BranchPicker.IsEnabled.Should().BeFalse();
        form.MergeBasePartnerPicker.CanonicalRepoPath.Should().BeNull();
        form.MergeBasePartnerPicker.IsEnabled.Should().BeFalse();
    }

    // === Default-partner auto-seed (A2) ===

    [Fact]
    public void Seed_OnConstruction_WhenPrefilledRepoAndPartnerEmpty_SetsPartnerToOriginDefault()
    {
        // Dominant case: dialog opens with prefilled repo (the active
        // context's path) and an empty partner. The form should query
        // the enumerator's origin/HEAD and pre-fill the partner so the
        // user only needs to type the branch and hit OK.
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        var e = new FakeEnumerator();
        e.DefaultRemoteBranchByRepo[@"C:\repo"] = "origin/main";

        var form = new BranchVsMergeBaseFormViewModel(Deps(v, e, prefilledRepoPath: @"C:\repo"));

        form.MergeBasePartner.Should().Be("origin/main");
    }

    [Fact]
    public void Seed_OnConstruction_WhenEnumeratorReturnsNull_LeavesPartnerEmpty()
    {
        // Repos without origin/HEAD (older clones, no remote, etc.) —
        // enumerator returns null and the form leaves the field blank
        // for the user to type. No crash, no surprise default.
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo"] = new RepoPathValidation.Valid(@"C:\repo");
        var e = new FakeEnumerator();
        // No entry in DefaultRemoteBranchByRepo => TryGetDefaultRemoteBranch returns null.

        var form = new BranchVsMergeBaseFormViewModel(Deps(v, e, prefilledRepoPath: @"C:\repo"));

        form.MergeBasePartner.Should().BeEmpty();
    }

    [Fact]
    public void Seed_NeverOverwrites_NonEmptyPartner()
    {
        // User explicitly set partner (or it was carried forward via
        // form caching after a mode switch). Switching repos must not
        // overwrite a partner the user already chose.
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo-a"] = new RepoPathValidation.Valid(@"C:\repo-a");
        v.RepoResults[@"C:\repo-b"] = new RepoPathValidation.Valid(@"C:\repo-b");
        var e = new FakeEnumerator();
        e.DefaultRemoteBranchByRepo[@"C:\repo-a"] = "origin/main";
        e.DefaultRemoteBranchByRepo[@"C:\repo-b"] = "origin/master";

        var form = new BranchVsMergeBaseFormViewModel(Deps(v, e, prefilledRepoPath: @"C:\repo-a"))
        {
            // Override the auto-seed with a custom value before
            // switching repos. (In practice the user would type this
            // — here we just write the property directly.)
            MergeBasePartner = "release/2025-q4",
        };

        form.RepoPath = @"C:\repo-b";

        form.MergeBasePartner.Should().Be("release/2025-q4",
            "non-empty partner is a positive user choice and must not be overwritten on repo switch");
    }

    [Fact]
    public void Seed_OnRepoSwitch_AfterUserClearedPartner_ReSeedsForNewRepo()
    {
        // Per the design call (A2 Option 1): empty partner is never a
        // positive choice (HasRequiredInputs requires it non-empty), so
        // when the user clears partner then switches repos, re-filling
        // it with the new repo's default is help, not magic.
        var v = new FakeValidator();
        v.RepoResults[@"C:\repo-a"] = new RepoPathValidation.Valid(@"C:\repo-a");
        v.RepoResults[@"C:\repo-b"] = new RepoPathValidation.Valid(@"C:\repo-b");
        var e = new FakeEnumerator();
        e.DefaultRemoteBranchByRepo[@"C:\repo-a"] = "origin/main";
        e.DefaultRemoteBranchByRepo[@"C:\repo-b"] = "origin/master";

        var form = new BranchVsMergeBaseFormViewModel(Deps(v, e, prefilledRepoPath: @"C:\repo-a"));
        form.MergeBasePartner.Should().Be("origin/main");

        form.MergeBasePartner = string.Empty;
        form.RepoPath = @"C:\repo-b";

        form.MergeBasePartner.Should().Be("origin/master");
    }

    [Fact]
    public void Seed_OnConstruction_WhenRepoPathInvalid_LeavesPartnerEmpty()
    {
        // Repo path doesn't canonicalize, so we can't ask the
        // enumerator anything. Partner stays empty; user will edit
        // RepoPath and the seed re-attempts on the next change.
        var v = new FakeValidator();
        v.RepoResults[@"C:\nope"] = new RepoPathValidation.Invalid("not a repo");
        var e = new FakeEnumerator();
        e.DefaultRemoteBranchByRepo[@"C:\nope"] = "origin/main"; // ignored

        var form = new BranchVsMergeBaseFormViewModel(Deps(v, e, prefilledRepoPath: @"C:\nope"));

        form.MergeBasePartner.Should().BeEmpty();
    }
}
