using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// "Working tree vs HEAD" form. Single required input: a repo path.
/// On submit, builds the same <see cref="ParsedCommandLine"/> the CLI
/// produces for an argv of <c>[repoPath]</c>:
/// <c>left = CommitIsh("HEAD"), right = WorkingTree</c>.
/// </summary>
public sealed partial class WorkingTreeVsHeadFormViewModel : NewDiffFormViewModelBase
{
    /// <summary>The validated repo root, populated by
    /// <see cref="NewDiffFormViewModelBase.ComputeValidationError"/>.
    /// Reset to null on every input change.</summary>
    private string? _canonicalRepoPath;

    [ObservableProperty]
    private string _repoPath;

    public WorkingTreeVsHeadFormViewModel(IDiffLaunchValidator validator, string? prefilledRepoPath = null)
        : base(validator)
    {
        _repoPath = prefilledRepoPath ?? string.Empty;
        Validate();
    }

    partial void OnRepoPathChanged(string value) => Validate();

    protected override bool HasRequiredInputs => !string.IsNullOrWhiteSpace(RepoPath);

    protected override string? ComputeValidationError()
    {
        _canonicalRepoPath = null;
        if (string.IsNullOrWhiteSpace(RepoPath)) return null;

        var result = Validator.ValidateRepoPath(RepoPath);
        if (result is RepoPathValidation.Valid v)
        {
            _canonicalRepoPath = v.CanonicalPath;
            return null;
        }
        return ((RepoPathValidation.Invalid)result).Message;
    }

    public override DiffLaunchSource BuildLaunchSource()
    {
        var parsed = new ParsedCommandLine(
            _canonicalRepoPath ?? RepoPath,
            new DiffSide.CommitIsh("HEAD"),
            new DiffSide.WorkingTree());
        return new DiffLaunchSource.Local(parsed);
    }
}
