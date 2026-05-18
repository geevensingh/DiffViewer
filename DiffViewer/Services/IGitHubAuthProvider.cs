namespace DiffViewer.Services;

/// <summary>
/// Resolves a GitHub auth token for a given host. The v1 implementation is
/// <see cref="GhCliAuthProvider"/>, which shells out to <c>gh auth token
/// --hostname &lt;host&gt;</c>. A PAT-in-settings fallback is explicitly out of
/// scope for v1 (see plan D3); when that lands it'll register a different
/// implementation of this same interface.
/// </summary>
/// <remarks>
/// <para>Implementations must be free-threaded — callers (the API client,
/// the PR resolver) may live on any thread.</para>
///
/// <para>Tokens are cached for the process lifetime to avoid spawning
/// <c>gh</c> on every API call. <see cref="InvalidateCache"/> is invoked by
/// <see cref="IGitHubClient"/> after a 401 so a token rotated in another
/// shell (e.g., <c>gh auth refresh</c>) is picked up without restarting
/// DiffViewer.</para>
/// </remarks>
public interface IGitHubAuthProvider
{
    /// <summary>
    /// Returns a cached or freshly resolved auth token for <paramref name="host"/>,
    /// or <c>null</c> if no token is available (gh not installed, gh not
    /// authenticated for the host, etc.). Implementations must not throw
    /// for the common "gh missing" / "host not configured" cases; reserve
    /// exceptions for truly unexpected failures.
    /// </summary>
    Task<string?> TryGetTokenAsync(string host, CancellationToken ct);

    /// <summary>
    /// Drop the cached token for <paramref name="host"/>. The next
    /// <see cref="TryGetTokenAsync"/> call re-resolves from the underlying
    /// source (e.g., re-runs <c>gh auth token</c>).
    /// </summary>
    void InvalidateCache(string host);
}
