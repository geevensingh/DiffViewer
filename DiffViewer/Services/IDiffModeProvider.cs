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
    /// <param name="validator">Shared validator seam — same one every
    /// form uses, so validation matches CLI-parser semantics.</param>
    /// <param name="prefilledRepoPath">When non-null, the form should
    /// pre-fill its repo-path input with this canonical path so
    /// "compare another two commits in this repo" is one-field-away.
    /// PR-URL forms typically ignore this.</param>
    NewDiffFormViewModelBase CreateForm(IDiffLaunchValidator validator, string? prefilledRepoPath);
}
