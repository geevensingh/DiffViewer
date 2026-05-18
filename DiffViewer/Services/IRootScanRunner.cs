using DiffViewer.Models;

namespace DiffViewer.Services;

/// <summary>
/// Runs the per-root scan in <see cref="LocalRepoLocator"/> with a
/// timeout. Production uses <see cref="TaskRunRootScanRunner"/>, which
/// dispatches the scan to the .NET thread pool. Tests inject a
/// deterministic fake to avoid coupling test correctness to thread-pool
/// scheduling latency on contended CI runners.
/// </summary>
internal interface IRootScanRunner
{
    /// <summary>
    /// Run <paramref name="scan"/> for <paramref name="root"/> with the
    /// given <paramref name="timeout"/>. Returns <c>true</c> on
    /// completion with the scan's results; returns <c>false</c> on
    /// timeout (the scan may still be running in the background and
    /// its result is discarded).
    /// </summary>
    bool TryRunWithTimeout(
        string root,
        Func<IReadOnlyList<(RepoUrlKey Key, string Path)>> scan,
        TimeSpan timeout,
        out IReadOnlyList<(RepoUrlKey Key, string Path)> result);
}

internal sealed class TaskRunRootScanRunner : IRootScanRunner
{
    public bool TryRunWithTimeout(
        string root,
        Func<IReadOnlyList<(RepoUrlKey Key, string Path)>> scan,
        TimeSpan timeout,
        out IReadOnlyList<(RepoUrlKey Key, string Path)> result)
    {
        _ = root; // not needed by the production implementation
        var task = Task.Run(scan);
        if (!task.Wait(timeout))
        {
            result = Array.Empty<(RepoUrlKey, string)>();
            return false;
        }
        result = task.Result;
        return true;
    }
}
