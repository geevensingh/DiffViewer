using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Auto-update lifecycle for DiffViewer. Today there is one production
/// implementation, <see cref="VelopackUpdateService"/> (used when the
/// app is running from a Velopack-installed location), and a no-op
/// fallback <see cref="NullUpdateService"/> (used when the app is
/// running portable or from <c>dotnet run</c>).
///
/// <para>The Phase 2.3 surface splits Check / Download / Apply so
/// <see cref="DiffViewer.ViewModels.UpdateNotificationViewModel"/>
/// can sequence them across user interaction (the banner's
/// <c>Install</c> button fires Download → ApplyOnNextLaunch on
/// demand for <see cref="Models.AutoUpdateMode.NotifyOnly"/>; the
/// <c>Automatic</c> path chains all three in the background).
/// </para>
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Check the configured update source for a newer release. Returns
    /// <see cref="UpdateCheckResult.NoUpdateAvailable"/> when nothing
    /// is available, or a populated result whose
    /// <see cref="UpdateCheckResult.OpaqueHandle"/> the same service
    /// will consume on follow-up <see cref="DownloadAsync"/> /
    /// <see cref="ApplyOnNextLaunchAsync"/> calls.
    ///
    /// <para>Best-effort — network failures, missing release feed, and
    /// "app is not running in an installed location" all degrade to
    /// <see cref="UpdateCheckResult.NoUpdateAvailable"/> rather than
    /// throwing.</para>
    /// </summary>
    Task<UpdateCheckResult> CheckAsync(CancellationToken ct);

    /// <summary>
    /// Download the bits for a previously-detected update. Idempotent
    /// (safe to call multiple times for the same
    /// <paramref name="update"/>). Tolerant of being called with
    /// <see cref="UpdateCheckResult.NoUpdateAvailable"/> — degrades
    /// to a no-op.
    /// </summary>
    Task DownloadAsync(UpdateCheckResult update, CancellationToken ct);

    /// <summary>
    /// Queue the downloaded update to apply silently when the app next
    /// exits cleanly. The user keeps using the current version until
    /// they close the app; on next launch they get the new version.
    /// Tolerant of being called with
    /// <see cref="UpdateCheckResult.NoUpdateAvailable"/> — degrades
    /// to a no-op.
    /// </summary>
    Task ApplyOnNextLaunchAsync(UpdateCheckResult update, CancellationToken ct);
}

