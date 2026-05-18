using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// "Working tree vs commit" form. Two required inputs: repo path and a
/// commit-ish. Builds a <see cref="ParsedCommandLine"/> matching the
/// CLI's <c>[repoPath, commitIsh]</c> argv:
/// <c>left = CommitIsh(commitIsh), right = WorkingTree</c>.
/// </summary>
public sealed partial class WorkingTreeVsCommitFormViewModel : NewDiffFormViewModelBase
{
    private string? _canonicalRepoPath;

    [ObservableProperty]
    private string _repoPath;

    [ObservableProperty]
    private string _commitIsh;

    public WorkingTreeVsCommitFormViewModel(IDiffLaunchValidator validator, string? prefilledRepoPath = null)
        : base(validator)
    {
        _repoPath = prefilledRepoPath ?? string.Empty;
        _commitIsh = string.Empty;
        Validate();
    }

    partial void OnRepoPathChanged(string value) => Validate();
    partial void OnCommitIshChanged(string value) => Validate();

    protected override bool HasRequiredInputs =>
        !string.IsNullOrWhiteSpace(RepoPath) && !string.IsNullOrWhiteSpace(CommitIsh);

    protected override string? ComputeValidationError()
    {
        _canonicalRepoPath = null;
        if (string.IsNullOrWhiteSpace(RepoPath) || string.IsNullOrWhiteSpace(CommitIsh)) return null;

        var repoResult = Validator.ValidateRepoPath(RepoPath);
        if (repoResult is not RepoPathValidation.Valid v)
        {
            return ((RepoPathValidation.Invalid)repoResult).Message;
        }
        _canonicalRepoPath = v.CanonicalPath;

        var commitResult = Validator.ValidateCommitIsh(v.CanonicalPath, CommitIsh);
        return commitResult is CommitIshValidation.Invalid invalid ? invalid.Message : null;
    }

    public override DiffLaunchSource BuildLaunchSource()
    {
        var parsed = new ParsedCommandLine(
            _canonicalRepoPath ?? RepoPath,
            new DiffSide.CommitIsh(CommitIsh),
            new DiffSide.WorkingTree());
        return new DiffLaunchSource.Local(parsed);
    }
}
