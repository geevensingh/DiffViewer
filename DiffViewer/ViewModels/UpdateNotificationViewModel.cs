using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffViewer.Models;
using DiffViewer.Services;

namespace DiffViewer.ViewModels;

/// <summary>
/// Backs the auto-update banner shown at the top of the main window.
/// Owns a small state machine over <see cref="IUpdateService"/>:
///
/// <list type="number">
///   <item>Hidden by default (<see cref="IsBannerVisible"/> = <c>false</c>).</item>
///   <item><see cref="StartAsync"/> probes <see cref="IUpdateService.CheckAsync"/>
///     in the background. If no update is available, the banner stays
///     hidden.</item>
///   <item>When an update is available, behavior branches on the user's
///     persisted <see cref="AutoUpdateMode"/>:
///     <list type="bullet">
///       <item><see cref="AutoUpdateMode.Disabled"/>: no check fires
///         (the <see cref="App"/>-level gate short-circuits before
///         <see cref="StartAsync"/> is even invoked).</item>
///       <item><see cref="AutoUpdateMode.Automatic"/>: download +
///         apply-on-next-launch fire silently. Banner becomes visible
///         once the queue is set so the user knows "v1.5.0 will
///         install when you next close DiffViewer".</item>
///       <item><see cref="AutoUpdateMode.NotifyOnly"/>: banner becomes
///         visible immediately with an Install button. Click ->
///         download + apply-on-next-launch.</item>
///     </list>
///   </item>
///   <item>Dismiss hides the banner for the rest of the session.
///     Persisting "skip this version forever" is Phase 2.4 follow-up
///     work — Phase 2.3's Dismiss is session-scoped only.</item>
/// </list>
///
/// <para>This view-model is constructed once at startup (lifecycle
/// owned by <see cref="App"/>) and held by the main window. It is
/// independent of the per-context <see cref="MainViewModel"/> graph
/// because updates are an app-wide concern, not a per-diff one.</para>
/// </summary>
public sealed partial class UpdateNotificationViewModel : ObservableObject
{
    private readonly IUpdateService _updates;
    private readonly Func<AutoUpdateMode> _getMode;

    private UpdateCheckResult? _pending;

    public UpdateNotificationViewModel(IUpdateService updates, Func<AutoUpdateMode> getMode)
    {
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _getMode = getMode ?? throw new ArgumentNullException(nameof(getMode));
    }

    /// <summary>Whether the banner is shown in the main window.</summary>
    [ObservableProperty] private bool _isBannerVisible;

    /// <summary>
    /// Single-line text shown in the banner. Examples: "Update available:
    /// v1.5.0", "Downloading update...", "Update v1.5.0 ready — will
    /// install on next launch".
    /// </summary>
    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>
    /// Whether the banner's Install button is shown. Only true in
    /// <see cref="AutoUpdateMode.NotifyOnly"/> before the user has
    /// accepted the update.
    /// </summary>
    [ObservableProperty] private bool _showInstallButton;

    /// <summary>
    /// Background entry point fired from <see cref="App"/> startup
    /// after the main window is shown. Runs the Check / Download /
    /// Apply sequence appropriate to the current
    /// <see cref="AutoUpdateMode"/>.
    /// </summary>
    public async Task StartAsync(CancellationToken ct)
    {
        var mode = _getMode();
        if (mode == AutoUpdateMode.Disabled) return;

        var result = await _updates.CheckAsync(ct).ConfigureAwait(true);
        if (!result.IsAvailable || result.Version is null) return;

        _pending = result;

        switch (mode)
        {
            case AutoUpdateMode.Automatic:
                IsBannerVisible = true;
                StatusText = $"Update available: v{result.Version}. Downloading...";
                ShowInstallButton = false;
                await _updates.DownloadAsync(result, ct).ConfigureAwait(true);
                await _updates.ApplyOnNextLaunchAsync(result, ct).ConfigureAwait(true);
                StatusText = $"Update v{result.Version} ready — will install on next launch.";
                break;

            case AutoUpdateMode.NotifyOnly:
                IsBannerVisible = true;
                StatusText = $"Update available: v{result.Version}.";
                ShowInstallButton = true;
                break;
        }
    }

    /// <summary>
    /// Banner's "Install" button. Only meaningful in
    /// <see cref="AutoUpdateMode.NotifyOnly"/> when an update is
    /// pending. Downloads and queues the update; the banner stays
    /// visible to confirm the queued state.
    /// </summary>
    [RelayCommand]
    private async Task InstallAsync()
    {
        if (_pending is null) return;
        var pending = _pending;
        ShowInstallButton = false;
        StatusText = $"Downloading update v{pending.Version}...";
        await _updates.DownloadAsync(pending, CancellationToken.None).ConfigureAwait(true);
        await _updates.ApplyOnNextLaunchAsync(pending, CancellationToken.None).ConfigureAwait(true);
        StatusText = $"Update v{pending.Version} ready — will install on next launch.";
    }

    /// <summary>
    /// Banner's "Dismiss" button. Hides the banner for the rest of
    /// this session. Phase 2.4 will add a separate "Skip this
    /// version" gesture that persists across launches; until then,
    /// dismissing only suppresses the notification until next launch.
    /// </summary>
    [RelayCommand]
    private void Dismiss()
    {
        IsBannerVisible = false;
    }
}
