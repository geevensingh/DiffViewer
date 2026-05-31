using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffViewer.Models;
using DiffViewer.Services;
using DiffViewer.Utility;

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
///     Skip hides it and persists the skipped version so future
///     checks for the same version stay quiet across launches.</item>
///   <item>After each check / dismiss / skip, the VM schedules the next
///     periodic re-check based on
///     <see cref="UpdateCheckCadence"/>; <see cref="UpdateCheckCadence.StartupOnly"/>
///     opts out of periodic re-checks entirely.</item>
/// </list>
///
/// <para>This view-model is constructed once at startup (lifecycle
/// owned by <see cref="App"/>) and held by the main window. It is
/// independent of the per-context <see cref="MainViewModel"/> graph
/// because updates are an app-wide concern, not a per-diff one.</para>
/// </summary>
public sealed partial class UpdateNotificationViewModel : ObservableObject, IDisposable
{
    private readonly IUpdateService _updates;
    private readonly Func<AutoUpdateMode> _getMode;
    private readonly Func<UpdateCheckCadence> _getCadence;
    private readonly Func<string?> _getSkippedVersion;
    private readonly Action<string?> _setSkippedVersion;
    private readonly bool _useDispatcherTimer;

    private UpdateCheckResult? _pending;
    private DispatcherTimer? _timer;

    public UpdateNotificationViewModel(
        IUpdateService updates,
        Func<AutoUpdateMode> getMode,
        Func<UpdateCheckCadence> getCadence,
        Func<string?> getSkippedVersion,
        Action<string?> setSkippedVersion,
        bool useDispatcherTimer = true)
    {
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _getMode = getMode ?? throw new ArgumentNullException(nameof(getMode));
        _getCadence = getCadence ?? throw new ArgumentNullException(nameof(getCadence));
        _getSkippedVersion = getSkippedVersion ?? throw new ArgumentNullException(nameof(getSkippedVersion));
        _setSkippedVersion = setSkippedVersion ?? throw new ArgumentNullException(nameof(setSkippedVersion));
        _useDispatcherTimer = useDispatcherTimer;
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
    /// <see cref="AutoUpdateMode"/>, then schedules the next periodic
    /// re-check.
    /// </summary>
    public async Task StartAsync(CancellationToken ct)
    {
        var mode = _getMode();
        if (mode == AutoUpdateMode.Disabled)
        {
            ScheduleNextRecheck();
            return;
        }

        var result = await _updates.CheckAsync(ct).ConfigureAwait(true);
        if (!result.IsAvailable || result.Version is null)
        {
            ScheduleNextRecheck();
            return;
        }

        // Honor a previously-persisted skip — silently consume the
        // detection without changing visible state.
        if (string.Equals(_getSkippedVersion(), result.Version, StringComparison.Ordinal))
        {
            ScheduleNextRecheck();
            return;
        }

        _pending = result;

        // Demote Automatic to NotifyOnly when the underlying service
        // can't actually apply silently (Phase 5 — browser-notify
        // case). Surprise-launching a browser tab on every startup
        // would be hostile UX; instead we show the banner with an
        // Install button just like NotifyOnly so the user opts in
        // before the browser opens.
        var effectiveMode = mode;
        if (effectiveMode == AutoUpdateMode.Automatic && !_updates.CanAutoApply)
        {
            effectiveMode = AutoUpdateMode.NotifyOnly;
        }

        switch (effectiveMode)
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

        ScheduleNextRecheck();
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
    /// this session; the next launch's startup check will surface the
    /// notification again. For permanent suppression of a specific
    /// version, use <see cref="SkipCommand"/>.
    /// </summary>
    [RelayCommand]
    private void Dismiss()
    {
        IsBannerVisible = false;
    }

    /// <summary>
    /// Banner's "Skip this version" button. Persists the pending
    /// version into <see cref="AppSettings.SkippedUpdateVersion"/> via
    /// the <c>setSkippedVersion</c> callback wired by <see cref="App"/>,
    /// then hides the banner. Future checks for the same version
    /// stay quiet; checks for a different (newer) version overwrite
    /// the persisted skip and surface the banner again.
    /// </summary>
    [RelayCommand]
    private void Skip()
    {
        if (_pending?.Version is null) return;
        _setSkippedVersion(_pending.Version);
        IsBannerVisible = false;
    }

    /// <summary>
    /// Schedule the next periodic re-check using a
    /// <see cref="DispatcherTimer"/> driven by
    /// <see cref="UpdateCheckCadence"/>. <see cref="UpdateCheckCadence.StartupOnly"/>
    /// → no timer. Tests can opt out of the WPF dispatcher by
    /// constructing with <c>useDispatcherTimer: false</c>.
    /// </summary>
    private void ScheduleNextRecheck()
    {
        if (!_useDispatcherTimer) return;
        _timer?.Stop();

        var interval = _getCadence().ToInterval();
        if (interval is null) return; // StartupOnly opts out

        _timer = new DispatcherTimer { Interval = interval.Value };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (_timer is null) return;
        _timer.Stop();
        // Skip the recheck if the user is still looking at a previous
        // notification — they'll see the latest version after Dismiss
        // / Skip / Install. Otherwise rerun the StartAsync flow.
        if (IsBannerVisible)
        {
            ScheduleNextRecheck();
            return;
        }
        await StartAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }
    }
}
