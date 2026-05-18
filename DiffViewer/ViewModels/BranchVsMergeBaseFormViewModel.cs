using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// "Branch vs merge-base" form. The dominant code-review workflow:
/// compare a branch against the most recent common ancestor it
/// shares with a partner ref (usually <c>main</c> / <c>master</c> /
/// <c>origin/main</c>). The resulting comparison shows
/// <em>only</em> what the branch added since it forked from the
/// partner, which is what every PR-review UI shows by default —
/// stable even as the partner branch moves on.
///
/// <para>Two required commit-ish inputs:
/// <see cref="Branch"/> (right side; both the user's working
/// reference and the form's launchable right-side ref),
/// <see cref="MergeBasePartner"/> (the ref to find a common ancestor
/// with). On submit the form resolves
/// <see cref="IGitRefEnumerator.TryComputeMergeBase"/>; if it
/// succeeds, the launch source compares
/// <c>(left = mergeBaseSha, right = branch)</c> so additions land on
/// the right, matching the convention every other form follows. If
/// it fails (orphaned histories or unresolvable refs) the validation
/// error surfaces in the dialog footer and OK stays disabled.</para>
/// </summary>
public sealed partial class BranchVsMergeBaseFormViewModel : NewDiffFormViewModelBase
{
    private readonly IGitRefEnumerator _enumerator;
    private string? _canonicalRepoPath;
    private string? _resolvedMergeBaseSha;

    [ObservableProperty]
    private string _repoPath;

    [ObservableProperty]
    private string _branch;

    [ObservableProperty]
    private string _mergeBasePartner;

    public RefPickerViewModel BranchPicker { get; }
    public RefPickerViewModel MergeBasePartnerPicker { get; }

    public BranchVsMergeBaseFormViewModel(FormDependencies deps)
        : base(deps.Validator)
    {
        _enumerator = deps.RefEnumerator;
        _repoPath = deps.PrefilledRepoPath ?? string.Empty;
        _branch = string.Empty;
        _mergeBasePartner = string.Empty;
        BranchPicker = new RefPickerViewModel(
            deps.RefEnumerator, deps.RecentContexts,
            writeBack: value => Branch = value);
        MergeBasePartnerPicker = new RefPickerViewModel(
            deps.RefEnumerator, deps.RecentContexts,
            writeBack: value => MergeBasePartner = value);
        Validate();
        SyncPickerRepoPath();
    }

    partial void OnRepoPathChanged(string value)
    {
        Validate();
        SyncPickerRepoPath();
    }

    partial void OnBranchChanged(string value) => Validate();
    partial void OnMergeBasePartnerChanged(string value) => Validate();

    protected override bool HasRequiredInputs =>
        !string.IsNullOrWhiteSpace(RepoPath)
        && !string.IsNullOrWhiteSpace(Branch)
        && !string.IsNullOrWhiteSpace(MergeBasePartner);

    protected override string? ComputeValidationError()
    {
        _canonicalRepoPath = null;
        _resolvedMergeBaseSha = null;
        if (!HasRequiredInputs) return null;

        var repoResult = Validator.ValidateRepoPath(RepoPath);
        if (repoResult is not RepoPathValidation.Valid v)
        {
            return ((RepoPathValidation.Invalid)repoResult).Message;
        }
        _canonicalRepoPath = v.CanonicalPath;

        // Validate both refs first so the user sees the most
        // diagnostic error (an unresolvable ref) rather than the
        // less-informative "no common ancestor" that would come back
        // if we jumped straight to FindMergeBase.
        var branchResult = Validator.ValidateCommitIsh(v.CanonicalPath, Branch);
        var partnerResult = Validator.ValidateCommitIsh(v.CanonicalPath, MergeBasePartner);

        var branchError = (branchResult as CommitIshValidation.Invalid)?.Message;
        var partnerError = (partnerResult as CommitIshValidation.Invalid)?.Message;
        if (branchError is not null && partnerError is not null) return branchError + "\n" + partnerError;
        if (branchError is not null) return branchError;
        if (partnerError is not null) return partnerError;

        var mergeBase = _enumerator.TryComputeMergeBase(v.CanonicalPath, Branch, MergeBasePartner);
        if (mergeBase is null)
        {
            return $"No common ancestor between `{Branch}` and `{MergeBasePartner}`.";
        }
        _resolvedMergeBaseSha = mergeBase;
        return null;
    }

    public override DiffLaunchSource BuildLaunchSource()
    {
        // Caller must check IsValid first. If the merge-base hasn't
        // been resolved (form invalid) building the launch source is
        // a contract violation — throw an explicit message rather
        // than land an empty SHA in the parsed command line.
        if (_resolvedMergeBaseSha is null)
        {
            throw new System.InvalidOperationException(
                "Cannot build a DiffLaunchSource for an invalid Branch-vs-merge-base form.");
        }

        var parsed = new ParsedCommandLine(
            _canonicalRepoPath ?? RepoPath,
            new DiffSide.CommitIsh(_resolvedMergeBaseSha),
            new DiffSide.CommitIsh(Branch));
        return new DiffLaunchSource.Local(parsed);
    }

    private void SyncPickerRepoPath()
    {
        BranchPicker.CanonicalRepoPath = _canonicalRepoPath;
        MergeBasePartnerPicker.CanonicalRepoPath = _canonicalRepoPath;
    }
}
