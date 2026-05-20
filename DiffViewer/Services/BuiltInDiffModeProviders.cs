using DiffViewer.ViewModels;

namespace DiffViewer.Services;

/// <summary>"Working tree vs HEAD" provider — opens with no commit-ish input.</summary>
public sealed class WorkingTreeVsHeadProvider : IDiffModeProvider
{
    public const string ProviderId = "local.working-tree-vs-head";
    public string Id => ProviderId;
    public string DisplayName => "Working tree vs HEAD";

    public NewDiffFormViewModelBase CreateForm(FormDependencies deps)
        => new WorkingTreeVsHeadFormViewModel(deps);
}

/// <summary>"Working tree vs commit" provider — single commit-ish input.</summary>
public sealed class WorkingTreeVsCommitProvider : IDiffModeProvider
{
    public const string ProviderId = "local.working-tree-vs-commit";
    public string Id => ProviderId;
    public string DisplayName => "Working tree vs commit";

    public NewDiffFormViewModelBase CreateForm(FormDependencies deps)
        => new WorkingTreeVsCommitFormViewModel(deps);
}

/// <summary>"Commit vs commit" provider — base + compare commit-ish inputs.</summary>
public sealed class CommitVsCommitProvider : IDiffModeProvider
{
    public const string ProviderId = "local.commit-vs-commit";
    public string Id => ProviderId;
    public string DisplayName => "Commit vs commit";

    public NewDiffFormViewModelBase CreateForm(FormDependencies deps)
        => new CommitVsCommitFormViewModel(deps);
}

/// <summary>"Branch vs merge-base" provider — branch + merge-base
/// partner inputs. One-click setup for the dominant PR-style
/// "what did this branch add since it forked from main" workflow.</summary>
public sealed class BranchVsMergeBaseProvider : IDiffModeProvider
{
    public const string ProviderId = "local.branch-vs-merge-base";
    public string Id => ProviderId;
    public string DisplayName => "Branch vs merge-base";

    public NewDiffFormViewModelBase CreateForm(FormDependencies deps)
        => new BranchVsMergeBaseFormViewModel(deps);
}

/// <summary>"GitHub pull request" provider — single PR-URL input.</summary>
public sealed class GitHubPullRequestProvider : IDiffModeProvider
{
    public const string ProviderId = "github.pr";
    public string Id => ProviderId;
    public string DisplayName => "GitHub pull request";

    public NewDiffFormViewModelBase CreateForm(FormDependencies deps)
        => new GitHubPullRequestFormViewModel(deps);
}

/// <summary>"View stash" provider — inline stash list. One-click setup
/// for viewing a stash's changes against its parent (HEAD at stash
/// time), matching <c>git stash show</c> semantics.</summary>
public sealed class ViewStashProvider : IDiffModeProvider
{
    public const string ProviderId = "local.view-stash";
    public string Id => ProviderId;
    public string DisplayName => "View stash";

    public NewDiffFormViewModelBase CreateForm(FormDependencies deps)
        => new ViewStashFormViewModel(deps);
}
