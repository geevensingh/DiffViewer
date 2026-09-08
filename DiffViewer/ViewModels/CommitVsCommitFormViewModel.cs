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
public sealed partial class CommitVsCommitFormViewModel : LocalRepoFormViewModelBase
{
    [ObservableProperty]
    private string _baseCommit;

    [ObservableProperty]
    private string _compareCommit;

    public RefPickerViewModel BaseCommitPicker { get; }
    public RefPickerViewModel CompareCommitPicker { get; }

    public CommitVsCommitFormViewModel(FormDependencies deps)
        : base(deps)
    {
        _baseCommit = string.Empty;
        _compareCommit = string.Empty;
        BaseCommitPicker = new RefPickerViewModel(
            deps.RefEnumerator, deps.RecentContexts,
            writeBack: value => BaseCommit = value);
        CompareCommitPicker = new RefPickerViewModel(
            deps.RefEnumerator, deps.RecentContexts,
            writeBack: value => CompareCommit = value);
        InitializeRepoPath();
    }

    partial void OnBaseCommitChanged(string value) => Validate();
    partial void OnCompareCommitChanged(string value) => Validate();

    protected override bool HasRequiredLocalInputs =>
        !string.IsNullOrWhiteSpace(BaseCommit)
        && !string.IsNullOrWhiteSpace(CompareCommit);

    protected override void OnRepoPathResolved()
    {
        BaseCommitPicker.CanonicalRepoPath = CanonicalRepoPath;
        CompareCommitPicker.CanonicalRepoPath = CanonicalRepoPath;
    }

    protected override string? ComputeValidationError()
    {
        if (!HasRequiredInputs) return null;
        if (RepoPathError is not null) return RepoPathError;

        // Validate BOTH commit-ish fields against the canonical repo
        // path and surface every error at once. Stopping at the first
        // invalid ref hides the fact that the second one is also bad —
        // which is the opposite of helpful when the user mistyped both
        // (or, more commonly, used a default-branch name like `main`
        // for a repo whose default branch is `master`).
        var baseResult = Validator.ValidateCommitIsh(CanonicalRepoPath!, BaseCommit);
        var compareResult = Validator.ValidateCommitIsh(CanonicalRepoPath!, CompareCommit);

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
            CanonicalRepoPath ?? RepoPath,
            new DiffSide.CommitIsh(BaseCommit),
            new DiffSide.CommitIsh(CompareCommit));
        return new DiffLaunchSource.Local(parsed);
    }
}
