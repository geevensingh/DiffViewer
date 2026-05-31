namespace DiffViewer.Models;

/// <summary>
/// How DiffViewer handles available updates from the configured update
/// source. Persisted as <c>AppSettings.AutoUpdate</c> in
/// <c>settings.json</c>; added in schema v7. The runtime effect of
/// each value evolves across phases of the auto-update rollout:
///
/// <list type="bullet">
///   <item><see cref="Automatic"/>: download new releases in the
///     background and apply them silently on the next clean exit.
///     Phase 2.3 will add a banner to surface "update available";
///     until then, this mode is fully silent.</item>
///   <item><see cref="NotifyOnly"/>: <b>Phase 2.2</b> — effectively
///     the same as <see cref="Disabled"/> (no banner UI exists yet
///     to surface notifications); <b>Phase 2.3 onward</b> — check,
///     download, but only apply when the user clicks the banner's
///     "install" action. The default for new installs.</item>
///   <item><see cref="Disabled"/>: skip the update check entirely.
///     Useful for tightly-controlled environments where updates are
///     managed externally, or users who prefer to drive their own
///     upgrade cadence by downloading new installers from the
///     Releases page.</item>
/// </list>
/// </summary>
public enum AutoUpdateMode
{
    Automatic,
    NotifyOnly,
    Disabled,
}
