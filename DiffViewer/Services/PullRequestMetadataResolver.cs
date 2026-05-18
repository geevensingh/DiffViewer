using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="IPullRequestMetadataResolver"/> that forwards to
/// <see cref="IGitHubClient"/>. The wrapping seam exists so the
/// orchestrator can be tested without standing up the HTTP plumbing.
/// </summary>
internal sealed class PullRequestMetadataResolver : IPullRequestMetadataResolver
{
    private readonly IGitHubClient _client;

    public PullRequestMetadataResolver(IGitHubClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public Task<PullRequestInfo> ResolveAsync(PullRequestRef pr, CancellationToken ct)
        => _client.GetPullRequestAsync(pr, ct);
}
