using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// "Commit vs commit" form. Three required inputs: repo path, base
/// commit-ish, compare commit-ish. Builds a <see cref="ParsedCommandLine"/>
/// matching the CLI's <c>[repoPath, base, compare]</c> argv.
///
/// <para>v1 = plain text inputs only. A "pick from log" dropdown /
/// picker is deferred to v2 per plan §3.</para>
/// </summary>
public sealed partial class CommitVsCommitFormViewModel : NewDiffFormViewModelBase
{
    private string? _canonicalRepoPath;

    [ObservableProperty]
    private string _repoPath;

    [ObservableProperty]
    private string _baseCommit;

    [ObservableProperty]
    private string _compareCommit;

    public CommitVsCommitFormViewModel(IDiffLaunchValidator validator, string? prefilledRepoPath = null)
        : base(validator)
    {
        _repoPath = prefilledRepoPath ?? string.Empty;
        _baseCommit = string.Empty;
        _compareCommit = string.Empty;
        Validate();
    }

    partial void OnRepoPathChanged(string value) => Validate();
    partial void OnBaseCommitChanged(string value) => Validate();
    partial void OnCompareCommitChanged(string value) => Validate();

    protected override bool HasRequiredInputs =>
        !string.IsNullOrWhiteSpace(RepoPath)
        && !string.IsNullOrWhiteSpace(BaseCommit)
        && !string.IsNullOrWhiteSpace(CompareCommit);

    protected override string? ComputeValidationError()
    {
        _canonicalRepoPath = null;
        if (!HasRequiredInputs) return null;

        var repoResult = Validator.ValidateRepoPath(RepoPath);
        if (repoResult is not RepoPathValidation.Valid v)
        {
            return ((RepoPathValidation.Invalid)repoResult).Message;
        }
        _canonicalRepoPath = v.CanonicalPath;

        // Validate BOTH commit-ish fields against the canonical repo
        // path and surface every error at once. Stopping at the first
        // invalid ref hides the fact that the second one is also bad —
        // which is the opposite of helpful when the user mistyped both
        // (or, more commonly, used a default-branch name like `main`
        // for a repo whose default branch is `master`).
        var baseResult = Validator.ValidateCommitIsh(v.CanonicalPath, BaseCommit);
        var compareResult = Validator.ValidateCommitIsh(v.CanonicalPath, CompareCommit);

        var baseError = (baseResult as CommitIshValidation.Invalid)?.Message;
        var compareError = (compareResult as CommitIshValidation.Invalid)?.Message;

        if (baseError is null && compareError is null) return null;
        if (baseError is not null && compareError is not null)
        {
            return baseError + "\n" + compareError;
        }
        return baseError ?? compareError;
    }

    public override DiffLaunchSource BuildLaunchSource()
    {
        var parsed = new ParsedCommandLine(
            _canonicalRepoPath ?? RepoPath,
            new DiffSide.CommitIsh(BaseCommit),
            new DiffSide.CommitIsh(CompareCommit));
        return new DiffLaunchSource.Local(parsed);
    }
}
