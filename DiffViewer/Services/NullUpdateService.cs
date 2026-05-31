using System.Threading;
using System.Threading.Tasks;

namespace DiffViewer.Services;

/// <summary>
/// No-op <see cref="IUpdateService"/> used when the app is not running
/// from a Velopack-installed location (portable build, or
/// <c>dotnet run</c> during development).
///
/// <para>Phase 2.1 deliberately ships this as a true no-op rather than
/// implementing the browser-notify behavior described in the master
/// plan. Reason: the browser-notify path needs UI surface (a
/// dismissable banner) that Phase 2.3 introduces — until then,
/// triggering a browser launch from a silent background check would
/// be hostile UX. Phase 5 will replace this with a real
/// <c>BrowserNotifyUpdateService</c> once the banner UI exists.
/// </para>
/// </summary>
public sealed class NullUpdateService : IUpdateService
{
    public Task CheckAndQueueUpdateAsync(CancellationToken ct) => Task.CompletedTask;
}
