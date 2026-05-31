using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Auto-update lifecycle for DiffViewer. Today there are three
/// production implementations:
///
/// <list type="bullet">
///   <item><see cref="VelopackUpdateService"/> — when the app is
///     running from a Velopack-installed location. Supports silent
///     auto-apply on next launch.</item>
///   <item><see cref="BrowserNotifyUpdateService"/> — when the app
///     is running portable. Detects updates via the GitHub Releases
///     REST API; cannot apply silently — the user must click
///     <em>Install</em> in the banner, which opens the Releases page
///     in their browser.</item>
///   <item><see cref="NullUpdateService"/> — used during development
///     (<c>dotnet run</c>) and as a defensive fallback. Pure no-op.</item>
/// </list>
///
/// <para>The Phase 2.3 surface splits Check / Download / Apply so
/// <see cref="DiffViewer.ViewModels.UpdateNotificationViewModel"/>
/// can sequence them across user interaction (the banner's
/// <c>Install</c> button fires Download → ApplyOnNextLaunch on
/// demand for <see cref="Models.AutoUpdateMode.NotifyOnly"/>; the
/// <c>Automatic</c> path chains all three in the background — but
/// only when <see cref="CanAutoApply"/> is <c>true</c>; otherwise
/// the VM silently demotes to the NotifyOnly flow so the
/// browser-notify path doesn't surprise-launch a browser tab on
/// every startup).</para>
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// <c>true</c> when <see cref="ApplyOnNextLaunchAsync"/> can be
    /// invoked silently in the background (Velopack-installed
    /// scenario); <c>false</c> when "apply" requires user-initiated
    /// action (BrowserNotify case — the apply action opens the
    /// browser, which is hostile UX without explicit consent).
    ///
    /// <para><see cref="DiffViewer.ViewModels.UpdateNotificationViewModel"/>
    /// checks this flag in the <see cref="Models.AutoUpdateMode.Automatic"/>
    /// branch: <c>true</c> → chain Check → Download → Apply silently;
    /// <c>false</c> → demote to the NotifyOnly flow (show the banner
    /// with an Install button instead of acting on the user's behalf).</para>
    /// </summary>
    bool CanAutoApply { get; }

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
    /// to a no-op. For
    /// <see cref="BrowserNotifyUpdateService"/>, the "download" is a
    /// no-op because the actual download happens in the user's
    /// browser after <see cref="ApplyOnNextLaunchAsync"/> opens the
    /// Releases page.
    /// </summary>
    Task DownloadAsync(UpdateCheckResult update, CancellationToken ct);

    /// <summary>
    /// For the Velopack adapter: queue the downloaded update to apply
    /// silently when the app next exits cleanly. For the browser-notify
    /// adapter: launch the user's default browser at the Releases
    /// page. Tolerant of being called with
    /// <see cref="UpdateCheckResult.NoUpdateAvailable"/> — degrades
    /// to a no-op. See <see cref="CanAutoApply"/> for why callers
    /// should check that flag in <see cref="Models.AutoUpdateMode.Automatic"/>
    /// before invoking this silently.
    /// </summary>
    Task ApplyOnNextLaunchAsync(UpdateCheckResult update, CancellationToken ct);
}

