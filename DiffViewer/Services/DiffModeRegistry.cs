using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DiffViewer.Services;

/// <summary>
/// Ordered, immutable list of the <see cref="IDiffModeProvider"/>s the
/// "New diff" dialog should offer. Holds one instance of each provider
/// for the app's lifetime — providers are stateless (they just create
/// form view-models on demand) so a singleton is fine.
///
/// <para>v1 ships with four built-in providers. A future ADO provider
/// adds one line in <see cref="BuildDefault"/> (or is composed in via
/// the explicit list ctor for tests).</para>
/// </summary>
public sealed class DiffModeRegistry
{
    public IReadOnlyList<IDiffModeProvider> Providers { get; }

    public DiffModeRegistry(IReadOnlyList<IDiffModeProvider> providers)
    {
        Providers = new ReadOnlyCollection<IDiffModeProvider>(
            new List<IDiffModeProvider>(providers ?? new List<IDiffModeProvider>()));
    }

    /// <summary>
    /// Production composition: the four built-in providers in the order
    /// they appear in the dialog's left rail. Order is intentional —
    /// "Working tree vs HEAD" is the cheapest interaction (one field,
    /// no commit-ish typing) so it leads.
    /// </summary>
    public static DiffModeRegistry BuildDefault() => new(new IDiffModeProvider[]
    {
        new WorkingTreeVsHeadProvider(),
        new WorkingTreeVsCommitProvider(),
        new CommitVsCommitProvider(),
        new GitHubPullRequestProvider(),
    });
}
