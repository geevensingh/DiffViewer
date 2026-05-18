using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Production orchestrator for <see cref="IPullRequestResolver"/>. Composes
/// <see cref="ILocalRepoLocator"/>, <see cref="IPullRequestMetadataResolver"/>,
/// and <see cref="IPullRequestLocalFetcher"/> into the launch contract the
/// coordinator wires up in Phase 8.
/// </summary>
internal sealed class PullRequestResolver : IPullRequestResolver
{
    private readonly ILocalRepoLocator _locator;
    private readonly IPullRequestMetadataResolver _metadata;
    private readonly IPullRequestLocalFetcher _fetcher;

    public PullRequestResolver(
        ILocalRepoLocator locator,
        IPullRequestMetadataResolver metadata,
        IPullRequestLocalFetcher fetcher)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
    }

    public async Task<PullRequestResolution> ResolveAsync(
        PullRequestRef pr, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pr);

        var lookup = _locator.TryLocate(pr.Host, pr.Owner, pr.Repo);
        if (lookup.Path is null)
        {
            // Coordinator owns the dialog choice — the resolver simply
            // surfaces the state. Phase 8 will re-invoke ResolveAsync
            // after the user has picked or cloned a path (settings will
            // have the mapping when we come back through).
            return new PullRequestResolution.MissingClone(pr);
        }

        PullRequestInfo info;
        try
        {
            info = await _metadata.ResolveAsync(pr, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (GitHubException ex)
        {
            return new PullRequestResolution.Failed(pr, ex.Message);
        }

        PullRequestFetchResult fetched;
        try
        {
            fetched = await _fetcher.FetchAsync(lookup.Path, info, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (PullRequestFetchException ex)
        {
            return new PullRequestResolution.Failed(pr, ex.Message);
        }

        var parsed = new ParsedCommandLine(
            lookup.Path,
            new DiffSide.CommitIsh(fetched.MergeBaseSha),
            new DiffSide.CommitIsh(fetched.HeadSha));

        return new PullRequestResolution.Ready(parsed, pr);
    }
}
