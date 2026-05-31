using System.Threading;
using System.Threading.Tasks;
using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// No-op <see cref="IUpdateService"/> used when the app is not running
/// from a Velopack-installed location (portable build, or
/// <c>dotnet run</c> during development).
///
/// <para>Phase 2.1 / 2.3 deliberately ship this as a true no-op rather
/// than implementing the browser-notify behavior described in the
/// master plan. Reason: the browser-notify path needs a different
/// UI shape (a one-click "open the Releases page" affordance, not
/// the Velopack banner's "downloading / install" state machine).
/// Phase 5 will replace this with a real
/// <c>BrowserNotifyUpdateService</c> once that affordance is
/// designed.</para>
/// </summary>
public sealed class NullUpdateService : IUpdateService
{
    public Task<UpdateCheckResult> CheckAsync(CancellationToken ct) =>
        Task.FromResult(UpdateCheckResult.NoUpdateAvailable);

    public Task DownloadAsync(UpdateCheckResult update, CancellationToken ct) =>
        Task.CompletedTask;

    public Task ApplyOnNextLaunchAsync(UpdateCheckResult update, CancellationToken ct) =>
        Task.CompletedTask;
}

