using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;
using DiffViewer.ViewModels;

namespace DiffViewer.Services;

/// <summary>
/// Seam over the modal missing-clone dialog so the
/// <see cref="DiffViewer.MainWindowCoordinator"/> can be tested without
/// standing up a WPF window. Production implementation
/// (<see cref="MissingClonePromptHost"/>) constructs
/// <see cref="MissingClonePromptViewModel"/> with its dependencies, hands
/// it to <see cref="DiffViewer.Views.MissingClonePromptDialog"/>, calls
/// <c>ShowDialog()</c>, and awaits the VM's
/// <see cref="MissingClonePromptViewModel.Completion"/> task.
/// </summary>
/// <remarks>
/// <para>The host is intentionally a single async method rather than a
/// "create view-model" + "show dialog" pair: the coordinator never needs
/// to bind the VM to anything other than the dialog itself, and the
/// production impl is the only place that knows the full dependency
/// graph (folder picker, confirm-dialog hooks, settings, repo inspector,
/// cloner). Inverting that would push the wiring into the coordinator,
/// which would defeat the testability goal.</para>
///
/// <para>Threading: invoked from the UI thread by the coordinator. The
/// implementation may marshal back to the dispatcher internally.</para>
/// </remarks>
public interface IMissingClonePromptHost
{
    /// <summary>
    /// Show the missing-clone dialog for <paramref name="pr"/> modally
    /// and return the user's choice. Cancellation tokens flow through to
    /// any in-flight clone the user kicked off.
    /// </summary>
    Task<MissingClonePromptResult> ShowAsync(PullRequestRef pr, CancellationToken ct = default);
}
