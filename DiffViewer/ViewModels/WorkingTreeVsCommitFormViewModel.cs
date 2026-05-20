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
    private string? _repoPathError;

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
        // Canonicalize the prefilled repo path BEFORE the first
        // Validate() so the picker is enabled on dialog open when
        // launching with an already-open context. Without this, the
        // user would have to touch the repo-path text box to wake the
        // picker up. Mirrors the OnRepoPathChanged ordering.
        TryUpdateCanonicalRepoPath();
        SyncPickerRepoPath();
        Validate();
    }

    partial void OnRepoPathChanged(string value)
    {
        TryUpdateCanonicalRepoPath();
        SyncPickerRepoPath();
        Validate();
    }

    partial void OnCommitIshChanged(string value) => Validate();

    protected override bool HasRequiredInputs =>
        !string.IsNullOrWhiteSpace(RepoPath) && !string.IsNullOrWhiteSpace(CommitIsh);

    /// <summary>
    /// Resolve the user's repo-path input into either a canonical
    /// repository root (stored in <see cref="_canonicalRepoPath"/>,
    /// consumed by the picker for branch enumeration and by
    /// <see cref="BuildLaunchSource"/>) or a deferred validation
    /// message (stored in <see cref="_repoPathError"/>, surfaced by
    /// <see cref="ComputeValidationError"/> once the rest of the form
    /// is populated). Runs exactly once per repo-path change so the
    /// picker stays in sync without re-validating on every keystroke
    /// into the commit-ish field. Decoupling canonicalization from
    /// <see cref="ComputeValidationError"/> is what makes the picker
    /// reachable before the user types a commit-ish — historically the
    /// canonical path was a side effect of validation, so the picker
    /// was perma-disabled on dialog open.
    /// </summary>
    private void TryUpdateCanonicalRepoPath()
    {
        _canonicalRepoPath = null;
        _repoPathError = null;
        if (string.IsNullOrWhiteSpace(RepoPath)) return;

        var result = Validator.ValidateRepoPath(RepoPath);
        if (result is RepoPathValidation.Valid v)
        {
            _canonicalRepoPath = v.CanonicalPath;
        }
        else
        {
            _repoPathError = ((RepoPathValidation.Invalid)result).Message;
        }
    }

    protected override string? ComputeValidationError()
    {
        // Suppress every error message until all required fields are
        // populated — friendlier UX than flashing "Cannot resolve foo"
        // while the user is mid-type. _repoPathError still drives the
        // picker's enablement via TryUpdateCanonicalRepoPath; here it
        // only governs what the dialog footer says.
        if (!HasRequiredInputs) return null;
        if (_repoPathError is not null) return _repoPathError;

        var commitResult = Validator.ValidateCommitIsh(_canonicalRepoPath!, CommitIsh);
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

    /// <summary>Push the latest canonical repo root into the picker.
    /// Null when <see cref="TryUpdateCanonicalRepoPath"/> couldn't
    /// resolve the user's input — the picker reads
    /// <see cref="RefPickerViewModel.IsEnabled"/> off this value so a
    /// null here disables the <c>Pick…</c> button.</summary>
    private void SyncPickerRepoPath()
    {
        CommitIshPicker.CanonicalRepoPath = _canonicalRepoPath;
    }
}
