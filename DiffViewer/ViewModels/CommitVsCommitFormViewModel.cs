using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// "Commit vs commit" form. Three required inputs: repo path, base
/// commit-ish, compare commit-ish. Builds a <see cref="ParsedCommandLine"/>
/// matching the CLI's <c>[repoPath, base, compare]</c> argv.
///
/// <para>Each commit-ish input gets an independent ref-picker popup
/// (<see cref="BaseCommitPicker"/> / <see cref="CompareCommitPicker"/>);
/// they share the form's canonical repo path so both target the same
/// repository's branches / tags / recent refs.</para>
/// </summary>
public sealed partial class CommitVsCommitFormViewModel : NewDiffFormViewModelBase
{
    private string? _canonicalRepoPath;
    private string? _repoPathError;

    [ObservableProperty]
    private string _repoPath;

    [ObservableProperty]
    private string _baseCommit;

    [ObservableProperty]
    private string _compareCommit;

    public RefPickerViewModel BaseCommitPicker { get; }
    public RefPickerViewModel CompareCommitPicker { get; }

    public CommitVsCommitFormViewModel(FormDependencies deps)
        : base(deps.Validator)
    {
        _repoPath = deps.PrefilledRepoPath ?? string.Empty;
        _baseCommit = string.Empty;
        _compareCommit = string.Empty;
        BaseCommitPicker = new RefPickerViewModel(
            deps.RefEnumerator, deps.RecentContexts,
            writeBack: value => BaseCommit = value);
        CompareCommitPicker = new RefPickerViewModel(
            deps.RefEnumerator, deps.RecentContexts,
            writeBack: value => CompareCommit = value);
        // Canonicalize the prefilled repo path BEFORE the first
        // Validate() so both pickers are enabled on dialog open when
        // launching with an already-open context. Mirrors the
        // OnRepoPathChanged ordering.
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

    partial void OnBaseCommitChanged(string value) => Validate();
    partial void OnCompareCommitChanged(string value) => Validate();

    protected override bool HasRequiredInputs =>
        !string.IsNullOrWhiteSpace(RepoPath)
        && !string.IsNullOrWhiteSpace(BaseCommit)
        && !string.IsNullOrWhiteSpace(CompareCommit);

    /// <summary>
    /// Resolve the user's repo-path input into either a canonical
    /// repository root (stored in <see cref="_canonicalRepoPath"/>,
    /// consumed by both pickers for branch enumeration and by
    /// <see cref="BuildLaunchSource"/>) or a deferred validation
    /// message (stored in <see cref="_repoPathError"/>, surfaced by
    /// <see cref="ComputeValidationError"/> once the rest of the form
    /// is populated). See the WorkingTreeVsCommit form's copy of this
    /// method for the bug-history rationale behind decoupling
    /// canonicalization from validation.
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
        if (!HasRequiredInputs) return null;
        if (_repoPathError is not null) return _repoPathError;

        // Validate BOTH commit-ish fields against the canonical repo
        // path and surface every error at once. Stopping at the first
        // invalid ref hides the fact that the second one is also bad —
        // which is the opposite of helpful when the user mistyped both
        // (or, more commonly, used a default-branch name like `main`
        // for a repo whose default branch is `master`).
        var baseResult = Validator.ValidateCommitIsh(_canonicalRepoPath!, BaseCommit);
        var compareResult = Validator.ValidateCommitIsh(_canonicalRepoPath!, CompareCommit);

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

    private void SyncPickerRepoPath()
    {
        BaseCommitPicker.CanonicalRepoPath = _canonicalRepoPath;
        CompareCommitPicker.CanonicalRepoPath = _canonicalRepoPath;
    }
}
