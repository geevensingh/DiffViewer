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

        var baseResult = Validator.ValidateCommitIsh(v.CanonicalPath, BaseCommit);
        if (baseResult is CommitIshValidation.Invalid bi) return bi.Message;

        var compareResult = Validator.ValidateCommitIsh(v.CanonicalPath, CompareCommit);
        return compareResult is CommitIshValidation.Invalid ci ? ci.Message : null;
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
