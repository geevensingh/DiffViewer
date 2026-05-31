namespace DiffViewer.Models;

/// <summary>
/// How often DiffViewer should poll the configured update source for
/// a newer release. Persisted as
/// <c>AppSettings.UpdateCheckCadence</c> in <c>settings.json</c>;
/// added in schema v7. Inert in Phase 2.2 — only the startup-time
/// check fires today, regardless of this setting. Phase 2.3 will add
/// a periodic re-check timer that honors the configured cadence.
///
/// <para>The five values cover the documented choices from the auto-update
/// design ("startup-only / 1h / 6h / 24h / 7d"). DiffViewer launches
/// are deliberate (it's not a chat client), so anything more frequent
/// than <see cref="Hourly"/> would be wasted polling against GitHub
/// rate limits.</para>
/// </summary>
public enum UpdateCheckCadence
{
    /// <summary>Only check at startup; no periodic re-check.</summary>
    StartupOnly,

    Hourly,
    EverySixHours,
    Daily,
    Weekly,
}
