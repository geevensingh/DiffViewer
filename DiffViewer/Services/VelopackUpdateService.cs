using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="IUpdateService"/> wired to Velopack 1.x.
/// Checks the configured GitHub Releases feed, downloads any newer
/// release, and queues it to apply silently on the next clean exit
/// via <see cref="UpdateManager.WaitExitThenApplyUpdates"/>.
///
/// <para>This is a thin pass-through adapter — by design there is no
/// branching logic to test. The interesting behavior (check API,
/// download, apply-on-exit semantics) lives inside Velopack itself
/// and was verified end-to-end by the Phase 1 spike on branch
/// <c>spike/velopack</c>. Per AGENTS.md §10 thin-wrapper carve-out,
/// this class is intentionally untested; <see cref="NullUpdateService"/>
/// has a smoke test that covers the dispatch decision in
/// <see cref="App"/> startup.</para>
///
/// <para>Constructor takes nothing today; the feed URL is hardcoded
/// because Phase 2.1 deliberately predates the
/// <c>AppSettings.IncludePreReleases</c> toggle that Phase 2.2 adds.
/// Once that setting lands, this service will read it from
/// <see cref="ISettingsService"/> and pass through to the
/// <see cref="GithubSource"/> ctor.</para>
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private const string ReleasesUrl = "https://github.com/geevensingh/DiffViewer";

    /// <summary>
    /// Returns a configured <see cref="VelopackUpdateService"/> when
    /// the app is running from a Velopack-installed location, else
    /// <c>null</c>. Callers (today: <see cref="App"/> startup) should
    /// substitute a <see cref="NullUpdateService"/> on null so the
    /// rest of the app can program against <see cref="IUpdateService"/>
    /// unconditionally. Tolerant of Velopack-side exceptions
    /// (corrupt locator state, missing files in the install folder,
    /// etc.) — those degrade to "treat as portable" rather than
    /// crashing app startup.
    /// </summary>
    public static VelopackUpdateService? TryCreateForInstalled()
    {
        try
        {
            var source = new GithubSource(ReleasesUrl, accessToken: null, prerelease: false);
            var probe = new UpdateManager(source);
            return probe.IsInstalled ? new VelopackUpdateService() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task CheckAndQueueUpdateAsync(CancellationToken ct)
    {
        try
        {
            var source = new GithubSource(ReleasesUrl, accessToken: null, prerelease: false);
            var mgr = new UpdateManager(source);
            if (!mgr.IsInstalled)
            {
                return;
            }

            var info = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
            {
                return;
            }

            await mgr.DownloadUpdatesAsync(info).ConfigureAwait(false);
            mgr.WaitExitThenApplyUpdates(info);
        }
        catch (Exception)
        {
            // Best-effort: network failures, missing release feed,
            // GitHub rate-limiting, etc. Worst case the user stays on
            // the current version and the next launch retries. Phase
            // 2.3 will wire a Velopack ILogger to a rolling file log
            // under %LocalAppData%\DiffViewer\logs\ so diagnostics are
            // recoverable.
        }
    }
}
