using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// "Working tree vs commit" form. Two required inputs: repo path and a
/// commit-ish. Builds a <see cref="ParsedCommandLine"/> matching the
/// CLI's <c>[repoPath, commitIsh]</c> argv:
/// <c>left = CommitIsh(commitIsh), right = WorkingTree</c>.
///
/// <para>The commit-ish input gets a ref-picker popup powered by
/// <see cref="CommitIshPicker"/>; whenever the repo-path resolves to a
/// canonical root, that path is pushed into the picker so its branch
/// / tag / recent-ref enumeration targets the right repo.</para>
/// </summary>
public sealed partial class WorkingTreeVsCommitFormViewModel : NewDiffFormViewModelBase
{
    private string? _canonicalRepoPath;

    [ObservableProperty]
    private string _repoPath;

    [ObservableProperty]
    private string _commitIsh;

    /// <summary>Ref-picker VM bound to the popup next to the commit-ish input.</summary>
    public RefPickerViewModel CommitIshPicker { get; }

    public WorkingTreeVsCommitFormViewModel(FormDependencies deps)
        : base(deps.Validator)
    {
        _repoPath = deps.PrefilledRepoPath ?? string.Empty;
        _commitIsh = string.Empty;
        CommitIshPicker = new RefPickerViewModel(
            deps.RefEnumerator,
            deps.RecentContexts,
            writeBack: value => CommitIsh = value);
        Validate();
        SyncPickerRepoPath();
    }

    partial void OnRepoPathChanged(string value)
    {
        Validate();
        SyncPickerRepoPath();
    }

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

    /// <summary>
    /// Push the latest validated repo root into the picker, OR a
    /// best-effort canonical form of the user's literal input when
    /// validation hasn't (yet) produced one — the picker only opens
    /// once the repo path is valid, so falling back to the raw input
    /// just keeps the picker's <c>IsEnabled</c> state in sync with
    /// the validator without re-running it here.
    /// </summary>
    private void SyncPickerRepoPath()
    {
        CommitIshPicker.CanonicalRepoPath = _canonicalRepoPath;
    }
}
