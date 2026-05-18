using CommunityToolkit.Mvvm.ComponentModel;
using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// "GitHub pull request" form. Single required input: a PR URL.
/// Validation delegates to <see cref="PullRequestRef.TryParse"/> via
/// <see cref="IDiffLaunchValidator.ValidatePullRequestUrl"/>; the
/// actual repo lookup + fetch happens later when the coordinator's
/// <see cref="IPullRequestResolver"/> runs on OK.
/// </summary>
public sealed partial class GitHubPullRequestFormViewModel : NewDiffFormViewModelBase
{
    private PullRequestRef? _parsedRef;

    [ObservableProperty]
    private string _pullRequestUrl;

    public GitHubPullRequestFormViewModel(FormDependencies deps)
        : base(deps.Validator)
    {
        _pullRequestUrl = string.Empty;
        Validate();
    }

    partial void OnPullRequestUrlChanged(string value) => Validate();

    protected override bool HasRequiredInputs => !string.IsNullOrWhiteSpace(PullRequestUrl);

    protected override string? ComputeValidationError()
    {
        _parsedRef = null;
        if (string.IsNullOrWhiteSpace(PullRequestUrl)) return null;

        var result = Validator.ValidatePullRequestUrl(PullRequestUrl);
        if (result is PullRequestUrlValidation.Valid v)
        {
            _parsedRef = v.Pr;
            return null;
        }
        return ((PullRequestUrlValidation.Invalid)result).Message;
    }

    public override DiffLaunchSource BuildLaunchSource()
    {
        // Caller must check IsValid before calling — if BuildLaunchSource
        // is invoked while _parsedRef is still null, that's a contract
        // violation on the dialog side; throw an explicit message rather
        // than NullReference.
        if (_parsedRef is null)
        {
            throw new System.InvalidOperationException(
                "Cannot build a DiffLaunchSource for an invalid PR-URL form.");
        }
        return new DiffLaunchSource.GitHubPullRequest(_parsedRef);
    }
}
