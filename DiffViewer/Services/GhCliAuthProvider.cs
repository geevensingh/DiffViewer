using System.ComponentModel;
using System.IO;

namespace DiffViewer.Services;

/// <summary>
/// <see cref="IGitHubAuthProvider"/> that shells out to
/// <c>gh auth token --hostname &lt;host&gt;</c>. Tokens are cached per host
/// for the process lifetime; <see cref="InvalidateCache"/> drops the
/// entry so the next call re-spawns <c>gh</c> (used by
/// <see cref="GitHubClient"/> on a 401 to pick up a rotated token without
/// requiring a DiffViewer restart).
/// </summary>
/// <remarks>
/// <para>Failures (gh missing, gh not authenticated, non-zero exit) are
/// reported as a returned <c>null</c>, not an exception. Callers route
/// the <c>null</c> case through the same UX surface that handles
/// "DiffViewer needs gh installed and authenticated to view PRs".</para>
///
/// <para>Only <b>successful</b> token resolutions are cached; failed
/// attempts retry on the next call. This matters because the typical
/// failure mode is "user hasn't installed gh yet" — they install gh, hit
/// the URL again, and expect it to work without restarting the app.</para>
/// </remarks>
internal sealed class GhCliAuthProvider : IGitHubAuthProvider
{
    private readonly IProcessRunner _runner;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _tokenCache = new(StringComparer.OrdinalIgnoreCase);

    public GhCliAuthProvider(IProcessRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<string?> TryGetTokenAsync(string host, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        lock (_gate)
        {
            if (_tokenCache.TryGetValue(host, out var cached))
            {
                return cached;
            }
        }

        ProcessRunResult result;
        try
        {
            result = await _runner
                .RunAsync("gh", new[] { "auth", "token", "--hostname", host }, ct)
                .ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            // gh.exe not on PATH.
            return null;
        }
        catch (FileNotFoundException)
        {
            // Same case, surfaced as FileNotFoundException on some shells.
            return null;
        }

        if (result.ExitCode != 0)
        {
            // gh ran but failed: "host not configured", "not logged in",
            // etc. Surface as missing token so the caller shows the
            // "install/authenticate gh" message.
            return null;
        }

        var token = result.Stdout.Trim();
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        lock (_gate)
        {
            _tokenCache[host] = token;
        }
        return token;
    }

    public void InvalidateCache(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        lock (_gate)
        {
            _tokenCache.Remove(host);
        }
    }
}
