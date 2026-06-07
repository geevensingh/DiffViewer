namespace DiffViewer.Models;

using DiffViewer.Services;

/// <summary>
/// Result of an ETag-aware poll of GitHub's PR endpoint.
///
/// <para><see cref="Info"/> is <c>null</c> when the server returned
/// HTTP 304 Not Modified — the caller should keep its existing
/// snapshot and treat the tick as a no-op.</para>
///
/// <para><see cref="ETag"/> is the server's strong validator for the
/// PR resource; the caller passes it back as <c>If-None-Match</c> on
/// the next poll so unchanged PRs cost one round-trip with zero
/// response body bytes. May be <c>null</c> if the server omits the
/// header (rare).</para>
///
/// <para><see cref="RateLimitRemaining"/> is the value of the
/// <c>X-RateLimit-Remaining</c> response header, used by the watcher
/// to back off when quota runs low. <c>null</c> when the header is
/// absent (e.g. some error responses).</para>
/// </summary>
/// <param name="Info">Parsed PR metadata, or <c>null</c> on 304.</param>
/// <param name="ETag">Server-supplied ETag for use on the next poll.</param>
/// <param name="RateLimitRemaining">Remaining requests in the current
/// rate-limit window.</param>
public sealed record PullRequestPolledResult(
    PullRequestInfo? Info,
    string? ETag,
    int? RateLimitRemaining);
