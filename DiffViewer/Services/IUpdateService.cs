using System.Threading;
using System.Threading.Tasks;

namespace DiffViewer.Services;

/// <summary>
/// Auto-update lifecycle for DiffViewer. Today there is one production
/// implementation, <see cref="VelopackUpdateService"/> (used when the
/// app is running from a Velopack-installed location), and a no-op
/// fallback <see cref="NullUpdateService"/> (used when the app is
/// running portable or from <c>dotnet run</c>).
///
/// <para>The Phase 2.1 surface is deliberately minimal: a single
/// "check, download, queue for next clean exit" method. Phase 2.3 will
/// expand it (split <c>Check</c> from <c>Download</c> from
/// <c>Apply</c>, expose a "skip this version" gesture, surface
/// progress) once the in-app notification banner needs those seams.
/// Until then, anything finer-grained would be speculative API design.
/// </para>
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Check the configured update source for a newer release and, if
    /// one is available, queue it to apply silently when the app next
    /// exits cleanly. Best-effort: network failures, missing release
    /// feed, and "app is not running in an installed location" are all
    /// swallowed (logged via Velopack's logger). The next launch
    /// retries.
    /// </summary>
    Task CheckAndQueueUpdateAsync(CancellationToken ct);
}
