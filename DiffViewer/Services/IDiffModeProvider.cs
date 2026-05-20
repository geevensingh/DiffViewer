using DiffViewer.ViewModels;

namespace DiffViewer.Services;

/// <summary>
/// One row in the "New diff" dialog's left-rail mode picker. Each
/// concrete provider knows its display name and how to construct its
/// per-mode form view-model.
///
/// <para><b>Scalability</b>: adding a new mode (e.g. ADO PR) is one new
/// provider class + one new form VM + (optionally) one new
/// <see cref="DiffViewer.Models.IReviewRef"/> implementation. The dialog,
/// the coordinator, and the recents service don't change.</para>
/// </summary>
public interface IDiffModeProvider
{
    /// <summary>
    /// Stable identifier persisted in user preferences (e.g. the
    /// last-used mode). Must be unique within the
    /// <see cref="DiffModeRegistry"/>. Convention:
    /// <c>"{family}.{shape}"</c> (e.g., <c>"local.commit-vs-commit"</c>,
    /// <c>"github.pr"</c>, future <c>"ado.pr"</c>).
    /// </summary>
    string Id { get; }

    /// <summary>Human-readable label shown in the left-rail ListBox.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Build a fresh form view-model for this mode. The dialog VM
    /// caches the form across selection changes so partial input
    /// survives switching modes and switching back.
    /// </summary>
    /// <param name="dependencies">Form-level services: the shared
    /// validator (so every form validates the same way the CLI parser
    /// does), the ref enumerator (powering the per-input ref picker),
    /// the recents service (for the picker's "Recent in this repo"
    /// group), and the optional prefilled repo path.</param>
    NewDiffFormViewModelBase CreateForm(FormDependencies dependencies);
}

/// <summary>
/// Services + seed state every "New diff" form may consume. Passed
/// by <see cref="NewDiffDialogViewModel"/> into
/// <see cref="IDiffModeProvider.CreateForm"/> so individual form VMs
/// don't grow their own constructor signatures every time the dialog
/// gains a new cross-form capability (today: the ref picker).
///
/// <para>Forms that don't use a particular dependency simply ignore
/// it. Adding a new shared seam later is one extra positional record
/// member; existing forms keep compiling.</para>
/// </summary>
/// <param name="Validator">Shared validator seam — same one every
/// form uses, so validation matches CLI-parser semantics.</param>
/// <param name="RefEnumerator">Powers the per-input ref picker
/// popup. Forms with commit-ish inputs construct one
/// <see cref="RefPickerViewModel"/> per input from this.</param>
/// <param name="RecentContexts">Source of "Recent refs in this repo"
/// for the picker (filtered + deduped per repo path; see
/// <see cref="RefPickerViewModel"/>).</param>
/// <param name="PrefilledRepoPath">When non-null, the form should
/// pre-fill its repo-path input with this canonical path so
/// "compare another two commits in this repo" is one-field-away.
/// PR-URL forms typically ignore this.</param>
/// <param name="SeedPullRequestUrl">When non-null, the GitHub PR
/// form should pre-fill its URL input with this string. Set by
/// <see cref="NewDiffDialogHost"/> when it detects a PR URL on
/// the clipboard at dialog-open time. Non-PR forms ignore it.</param>
public sealed record FormDependencies(
    IDiffLaunchValidator Validator,
    IGitRefEnumerator RefEnumerator,
    IRecentContextsService RecentContexts,
    string? PrefilledRepoPath = null,
    string? SeedPullRequestUrl = null);
