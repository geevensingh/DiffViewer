using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;

namespace DiffViewer;

/// <summary>
/// Abstraction the recents dropdown and the "New diff" dialog both use to
/// ask for an in-place context switch. Implemented by
/// <see cref="MainWindowCoordinator"/>; tests can substitute a fake.
///
/// <para><see cref="IsSwitching"/> + <see cref="INotifyPropertyChanged.PropertyChanged"/>
/// drive the dropdown's <c>IsEnabled</c> binding so the user can't kick
/// off a second switch on top of an in-flight one.</para>
/// </summary>
public interface IContextSwitcher : INotifyPropertyChanged
{
    /// <summary>True while a switch is being built, validated, swapped, or torn down.</summary>
    bool IsSwitching { get; }

    /// <summary>
    /// Build a fresh per-context graph for the supplied recent and swap
    /// it in atomically. Returns <c>true</c> on success; <c>false</c>
    /// when the recent failed to load (the dialog and any
    /// recents-removal flow have already happened).
    /// </summary>
    Task<bool> SwitchToRecentAsync(RecentLaunchContext recent, CancellationToken ct = default);

    /// <summary>
    /// Generic in-place switch driven by a <see cref="DiffLaunchSource"/>
    /// — the "New diff" dialog's entry point. Dispatches by source
    /// variant: local launches build the context directly; review
    /// launches (e.g. <see cref="DiffLaunchSource.GitHubPullRequest"/>)
    /// run the resolver, opening the missing-clone dialog if the
    /// local clone cannot be found. Returns <c>true</c> on success;
    /// <c>false</c> when the user has already seen the error and any
    /// recents-removal / missing-clone flow has run to completion.
    /// </summary>
    Task<bool> SwitchToAsync(DiffLaunchSource source, CancellationToken ct = default);
}
