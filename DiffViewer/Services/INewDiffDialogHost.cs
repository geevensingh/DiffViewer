using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Seam over the modal "New diff" dialog. Parallel structure to
/// <see cref="IMissingClonePromptHost"/>: the production
/// implementation owns the WPF window plumbing, while tests and the
/// coordinator can stub a fake to drive the success / cancel paths
/// without spinning up a WPF dispatcher.
///
/// <para>The host is a single async method, not a "create VM + show"
/// pair, for the same reasons described on
/// <see cref="IMissingClonePromptHost"/>: the dialog VM's dependency
/// graph (registry, validator, owner-window lookup) is known only to
/// the production impl.</para>
///
/// <para>Threading: invoked from the UI thread. The impl may marshal
/// internally; the returned task completes back on the UI thread.</para>
/// </summary>
public interface INewDiffDialogHost
{
    /// <summary>
    /// Show the "New diff" dialog modally and return the user's
    /// choice. Returns <c>null</c> on cancel.
    /// </summary>
    /// <param name="prefilledRepoPath">Optional pre-fill for the repo
    /// path field in the local-mode forms — typically the current
    /// context's repo path so "compare another two commits in this
    /// repo" is a one-field interaction.</param>
    Task<DiffLaunchSource?> ShowAsync(string? prefilledRepoPath, CancellationToken ct = default);
}
