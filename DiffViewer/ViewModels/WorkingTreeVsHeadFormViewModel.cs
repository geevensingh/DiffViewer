using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// "Working tree vs HEAD" form. Single required input: a repo path.
/// On submit, builds the same <see cref="ParsedCommandLine"/> the CLI
/// produces for an argv of <c>[repoPath]</c>:
/// <c>left = CommitIsh("HEAD"), right = WorkingTree</c>.
///
/// <para>Which worktree the path points at is not incidental here:
/// <c>HEAD</c> is per-worktree, so this form compares against a
/// different commit depending on the checkout chosen in the worktree
/// picker.</para>
/// </summary>
public sealed partial class WorkingTreeVsHeadFormViewModel : LocalRepoFormViewModelBase
{
    public WorkingTreeVsHeadFormViewModel(FormDependencies deps)
        : base(deps)
    {
        InitializeRepoPath();
    }

    protected override string? ComputeValidationError() => RepoPathError;

    public override DiffLaunchSource BuildLaunchSource()
    {
        var parsed = new ParsedCommandLine(
            CanonicalRepoPath ?? RepoPath,
            new DiffSide.CommitIsh("HEAD"),
            new DiffSide.WorkingTree());
        return new DiffLaunchSource.Local(parsed);
    }
}
