namespace DiffViewer.Models;

/// <summary>
/// Outcome of <see cref="DiffViewer.Services.IUpdateService.CheckAsync"/>.
/// Either <see cref="NoUpdateAvailable"/> (the singleton "nothing to
/// do" result) or a populated instance describing an available
/// update. The <see cref="OpaqueHandle"/> property carries an
/// adapter-specific token that the same service consumes on
/// follow-up <c>DownloadAsync</c> / <c>ApplyOnNextLaunchAsync</c>
/// calls — callers must not interpret it.
///
/// <para>Designed as an inert DTO so the <see cref="DiffViewer.ViewModels.UpdateNotificationViewModel"/>
/// state machine can pass it around without dragging in
/// Velopack-specific types. The <see cref="DiffViewer.Services.NullUpdateService"/>
/// only ever returns <see cref="NoUpdateAvailable"/>; the
/// <see cref="DiffViewer.Services.VelopackUpdateService"/> populates
/// <see cref="OpaqueHandle"/> with the Velopack <c>UpdateInfo</c>
/// for the detected release.</para>
/// </summary>
public sealed record UpdateCheckResult
{
    /// <summary>
    /// Shared "no update" sentinel. Equality-comparable as a record so
    /// callers can <c>result == UpdateCheckResult.NoUpdateAvailable</c>
    /// if they want a reference-style check; equivalently, check
    /// <see cref="IsAvailable"/>.
    /// </summary>
    public static readonly UpdateCheckResult NoUpdateAvailable = new()
    {
        IsAvailable = false,
        Version = null,
        OpaqueHandle = null,
    };

    /// <summary><c>true</c> when an update was detected; <c>false</c> otherwise.</summary>
    public bool IsAvailable { get; init; }

    /// <summary>
    /// Display string for the available version, e.g. <c>"1.5.0"</c>.
    /// Only meaningful when <see cref="IsAvailable"/> is <c>true</c>.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Adapter-specific update token (e.g. Velopack's <c>UpdateInfo</c>).
    /// Opaque to callers — they pass the same instance back into
    /// <see cref="DiffViewer.Services.IUpdateService.DownloadAsync"/>
    /// and <see cref="DiffViewer.Services.IUpdateService.ApplyOnNextLaunchAsync"/>
    /// so the service can act on the same detection without
    /// re-querying the remote.
    /// </summary>
    public object? OpaqueHandle { get; init; }
}
