using System;
using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;
using Velopack;
using Velopack.Sources;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="IUpdateService"/> wired to Velopack 1.x.
/// Constructed once at startup with the user's
/// <c>IncludePreReleases</c> preference; the same instance services
/// every Check / Download / Apply call for the app's lifetime so the
/// underlying <see cref="UpdateManager"/> can amortise its setup work.
///
/// <para>This is a thin pass-through adapter — by design there is no
/// branching logic to test. The interesting behavior (check API,
/// download, apply-on-exit semantics) lives inside Velopack itself
/// and was verified end-to-end by the Phase 1 spike on branch
/// <c>spike/velopack</c>. Per AGENTS.md §10 thin-wrapper carve-out,
/// this class is intentionally untested; <see cref="NullUpdateService"/>
/// and the higher-level
/// <see cref="DiffViewer.ViewModels.UpdateNotificationViewModel"/>
/// state machine are tested independently.</para>
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private const string ReleasesUrl = "https://github.com/geevensingh/DiffViewer";

    private readonly UpdateManager _mgr;

    private VelopackUpdateService(UpdateManager mgr)
    {
        _mgr = mgr;
    }

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
    /// <param name="includePreReleases">
    /// When <c>true</c>, the configured
    /// <see cref="GithubSource"/> will include pre-release tags
    /// (e.g. <c>v1.5.0-rc1</c>) in its lookup. Sourced from
    /// <see cref="DiffViewer.Models.AppSettings.IncludePreReleases"/>
    /// at startup.
    /// </param>
    public static VelopackUpdateService? TryCreateForInstalled(bool includePreReleases)
    {
        try
        {
            var source = new GithubSource(ReleasesUrl, accessToken: null, prerelease: includePreReleases);
            var mgr = new UpdateManager(source);
            return mgr.IsInstalled ? new VelopackUpdateService(mgr) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
    {
        try
        {
            var info = await _mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
            {
                return UpdateCheckResult.NoUpdateAvailable;
            }
            return new UpdateCheckResult
            {
                IsAvailable = true,
                Version = info.TargetFullRelease.Version.ToString(),
                OpaqueHandle = info,
            };
        }
        catch (Exception)
        {
            // Best-effort: network failures, GitHub rate-limiting, etc.
            // The next launch retries. Phase 2.4 will wire a Velopack
            // ILogger to a rolling file log under
            // %LocalAppData%\DiffViewer\logs\ so diagnostics are
            // recoverable; for now we swallow silently to match the
            // Phase 2.1 posture.
            return UpdateCheckResult.NoUpdateAvailable;
        }
    }

    public async Task DownloadAsync(UpdateCheckResult update, CancellationToken ct)
    {
        if (update.OpaqueHandle is not UpdateInfo info) return;
        try
        {
            await _mgr.DownloadUpdatesAsync(info).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Same posture as CheckAsync: swallow, retry next launch.
        }
    }

    public Task ApplyOnNextLaunchAsync(UpdateCheckResult update, CancellationToken ct)
    {
        if (update.OpaqueHandle is not UpdateInfo info) return Task.CompletedTask;
        try
        {
            _mgr.WaitExitThenApplyUpdates(info);
        }
        catch (Exception)
        {
            // Worst case: user keeps the current version until next
            // clean exit retries the whole flow.
        }
        return Task.CompletedTask;
    }
}

