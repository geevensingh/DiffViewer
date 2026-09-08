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
public sealed partial class WorkingTreeVsCommitFormViewModel : LocalRepoFormViewModelBase
{
    [ObservableProperty]
    private string _commitIsh;

    /// <summary>Ref-picker VM bound to the popup next to the commit-ish input.</summary>
    public RefPickerViewModel CommitIshPicker { get; }

    public WorkingTreeVsCommitFormViewModel(FormDependencies deps)
        : base(deps)
    {
        _commitIsh = string.Empty;
        CommitIshPicker = new RefPickerViewModel(
            deps.RefEnumerator,
            deps.RecentContexts,
            writeBack: value => CommitIsh = value);
        InitializeRepoPath();
    }

    partial void OnCommitIshChanged(string value) => Validate();

    protected override bool HasRequiredLocalInputs => !string.IsNullOrWhiteSpace(CommitIsh);

    protected override void OnRepoPathResolved() =>
        CommitIshPicker.CanonicalRepoPath = CanonicalRepoPath;

    protected override string? ComputeValidationError()
    {
        // Suppress every error message until all required fields are
        // populated — friendlier UX than flashing "Cannot resolve foo"
        // while the user is mid-type. RepoPathError still drives the
        // picker's enablement via the base's canonicalization; here it
        // only governs what the dialog footer says.
        if (!HasRequiredInputs) return null;
        if (RepoPathError is not null) return RepoPathError;

        var commitResult = Validator.ValidateCommitIsh(CanonicalRepoPath!, CommitIsh);
        return commitResult is CommitIshValidation.Invalid invalid ? invalid.Message : null;
    }

    public override DiffLaunchSource BuildLaunchSource()
    {
        var parsed = new ParsedCommandLine(
            CanonicalRepoPath ?? RepoPath,
            new DiffSide.CommitIsh(CommitIsh),
            new DiffSide.WorkingTree());
        return new DiffLaunchSource.Local(parsed);
    }
}
