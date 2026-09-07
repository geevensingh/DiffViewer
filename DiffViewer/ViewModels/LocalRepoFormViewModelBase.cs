using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// Base for every "New diff" form whose input starts with a local
/// repository path. Owns the repo-path property, its canonicalization,
/// and the worktree picker attached to it, so the concrete forms are
/// left holding only the inputs that actually differ between modes.
///
/// <para><b>Canonicalization is decoupled from validation</b>, and the
/// distinction is load-bearing. <see cref="CanonicalRepoPath"/> is
/// refreshed on every keystroke because the ref and worktree pickers
/// need a resolved repository the moment one is typed. The
/// corresponding error is stashed in <see cref="RepoPathError"/> rather
/// than surfaced immediately, so a half-typed path doesn't flash a
/// validation message under the field; concrete forms decide when to
/// surface it from <see cref="NewDiffFormViewModelBase.ComputeValidationError"/>.</para>
///
/// <para><b>Construction order matters.</b> Derived constructors must
/// finish initializing their own fields — including any
/// <see cref="RefPickerViewModel"/> instances — and then call
/// <see cref="InitializeRepoPath"/> as their last statement. That
/// resolves the prefilled path and fires <see cref="OnRepoPathResolved"/>
/// before the first <see cref="NewDiffFormViewModelBase.Validate"/>, which
/// is what leaves the pickers enabled on open when the dialog was
/// launched from an already-loaded context.</para>
/// </summary>
public abstract partial class LocalRepoFormViewModelBase : NewDiffFormViewModelBase
{
    protected LocalRepoFormViewModelBase(FormDependencies deps)
        : base(deps.Validator)
    {
        _repoPath = deps.PrefilledRepoPath ?? string.Empty;
        WorktreePicker = new WorktreePickerViewModel(
            deps.WorktreeEnumerator,
            writeBack: value => RepoPath = value);
    }

    [ObservableProperty]
    private string _repoPath;

    /// <summary>
    /// The validated repository root for the current <see cref="RepoPath"/>,
    /// or <c>null</c> when it doesn't resolve. Concrete forms pass this
    /// to the validator for their commit-ish inputs and into
    /// <see cref="NewDiffFormViewModelBase.BuildLaunchSource"/>.
    /// </summary>
    protected string? CanonicalRepoPath { get; private set; }

    /// <summary>
    /// Deferred validation message for <see cref="RepoPath"/>, or
    /// <c>null</c> when the path resolves. See the class remarks for why
    /// this is not surfaced eagerly.
    /// </summary>
    protected string? RepoPathError { get; private set; }

    /// <summary>Picker listing the other worktrees of this repository.</summary>
    public WorktreePickerViewModel WorktreePicker { get; }

    partial void OnRepoPathChanged(string value)
    {
        TryUpdateCanonicalRepoPath();
        SyncPickers();
        Validate();
    }

    /// <summary>
    /// Resolve the prefilled repo path and prime the pickers. Derived
    /// constructors call this last; see the class remarks.
    /// </summary>
    protected void InitializeRepoPath()
    {
        TryUpdateCanonicalRepoPath();
        SyncPickers();
        Validate();
    }

    /// <summary>
    /// Called after <see cref="CanonicalRepoPath"/> changes so a form can
    /// re-point its own ref pickers. The worktree picker is handled by
    /// the base; forms with no ref pickers need not override.
    /// </summary>
    protected virtual void OnRepoPathResolved()
    {
    }

    /// <summary>
    /// Extra required-input gate for the concrete form. The repo path
    /// itself is already covered.
    /// </summary>
    protected virtual bool HasRequiredLocalInputs => true;

    protected sealed override bool HasRequiredInputs =>
        !string.IsNullOrWhiteSpace(RepoPath) && HasRequiredLocalInputs;

    private void SyncPickers()
    {
        WorktreePicker.CanonicalRepoPath = CanonicalRepoPath;
        OnRepoPathResolved();
    }

    private void TryUpdateCanonicalRepoPath()
    {
        CanonicalRepoPath = null;
        RepoPathError = null;
        if (string.IsNullOrWhiteSpace(RepoPath)) return;

        var result = Validator.ValidateRepoPath(RepoPath);
        if (result is RepoPathValidation.Valid valid)
        {
            CanonicalRepoPath = valid.CanonicalPath;
        }
        else
        {
            RepoPathError = ((RepoPathValidation.Invalid)result).Message;
        }
    }
}
