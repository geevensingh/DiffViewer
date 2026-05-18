using DiffViewer.ViewModels;

namespace DiffViewer.Services;

/// <summary>"Working tree vs HEAD" provider — opens with no commit-ish input.</summary>
public sealed class WorkingTreeVsHeadProvider : IDiffModeProvider
{
    public const string ProviderId = "local.working-tree-vs-head";
    public string Id => ProviderId;
    public string DisplayName => "Working tree vs HEAD";

    public NewDiffFormViewModelBase CreateForm(IDiffLaunchValidator validator, string? prefilledRepoPath)
        => new WorkingTreeVsHeadFormViewModel(validator, prefilledRepoPath);
}

/// <summary>"Working tree vs commit" provider — single commit-ish input.</summary>
public sealed class WorkingTreeVsCommitProvider : IDiffModeProvider
{
    public const string ProviderId = "local.working-tree-vs-commit";
    public string Id => ProviderId;
    public string DisplayName => "Working tree vs commit";

    public NewDiffFormViewModelBase CreateForm(IDiffLaunchValidator validator, string? prefilledRepoPath)
        => new WorkingTreeVsCommitFormViewModel(validator, prefilledRepoPath);
}

/// <summary>"Commit vs commit" provider — base + compare commit-ish inputs.</summary>
public sealed class CommitVsCommitProvider : IDiffModeProvider
{
    public const string ProviderId = "local.commit-vs-commit";
    public string Id => ProviderId;
    public string DisplayName => "Commit vs commit";

    public NewDiffFormViewModelBase CreateForm(IDiffLaunchValidator validator, string? prefilledRepoPath)
        => new CommitVsCommitFormViewModel(validator, prefilledRepoPath);
}

/// <summary>"GitHub pull request" provider — single PR-URL input.</summary>
public sealed class GitHubPullRequestProvider : IDiffModeProvider
{
    public const string ProviderId = "github.pr";
    public string Id => ProviderId;
    public string DisplayName => "GitHub pull request";

    public NewDiffFormViewModelBase CreateForm(IDiffLaunchValidator validator, string? prefilledRepoPath)
        => new GitHubPullRequestFormViewModel(validator);
}
