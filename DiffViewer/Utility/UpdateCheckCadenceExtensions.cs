using System;
using DiffViewer.Models;

namespace DiffViewer.Utility;

/// <summary>
/// Maps <see cref="UpdateCheckCadence"/> values to the
/// <see cref="TimeSpan"/> the
/// <see cref="DiffViewer.ViewModels.UpdateNotificationViewModel"/>
/// timer uses for periodic re-checks. Pure function so the cadence
/// → interval contract is testable without spinning up a WPF
/// dispatcher.
/// </summary>
public static class UpdateCheckCadenceExtensions
{
    /// <summary>
    /// Returns the polling interval for <paramref name="cadence"/>,
    /// or <c>null</c> for <see cref="UpdateCheckCadence.StartupOnly"/>
    /// (the "no periodic re-check; only the startup-time check
    /// fires" case).
    /// </summary>
    public static TimeSpan? ToInterval(this UpdateCheckCadence cadence) => cadence switch
    {
        UpdateCheckCadence.StartupOnly => null,
        UpdateCheckCadence.Hourly => TimeSpan.FromHours(1),
        UpdateCheckCadence.EverySixHours => TimeSpan.FromHours(6),
        UpdateCheckCadence.Daily => TimeSpan.FromHours(24),
        UpdateCheckCadence.Weekly => TimeSpan.FromDays(7),
        _ => null,
    };
}
