using System.IO;
using LibGit2Sharp;

namespace DiffViewer.Services;

/// <summary>
/// Production <see cref="IGitHubCloner"/> wrapping LibGit2Sharp's
/// <see cref="Repository.Clone(string, string, CloneOptions)"/>. Authentication
/// is not provided in v1: the plan routes private repos through the
/// "Browse to existing clone" path because we don't want to ship a token
/// store inside DiffViewer and the user already has credentials wired up
/// to <c>git</c> / <c>gh</c> at the OS level.
/// </summary>
/// <remarks>
/// LibGit2Sharp raises <see cref="UserCancelledException"/> when the
/// transfer or checkout progress callback returns <c>false</c>, which is
/// how this implementation honors <see cref="CancellationToken"/>.
/// </remarks>
internal sealed class LibGit2GitHubCloner : IGitHubCloner
{
    public Task<CloneResult> CloneAsync(
        string cloneUrl,
        string destinationPath,
        IProgress<CloneProgress>? progress,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloneUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        return Task.Run(() => RunClone(cloneUrl, destinationPath, progress, ct), ct);
    }

    private static CloneResult RunClone(
        string cloneUrl,
        string destinationPath,
        IProgress<CloneProgress>? progress,
        CancellationToken ct)
    {
        int lastCheckoutCompleted = 0;
        int lastCheckoutTotal = 0;

        var options = new CloneOptions
        {
            FetchOptions =
            {
                OnTransferProgress = tp =>
                {
                    if (progress is not null)
                    {
                        progress.Report(new CloneProgress(
                            BytesReceived: (int)tp.ReceivedBytes,
                            TotalObjects: tp.TotalObjects,
                            IndexedObjects: tp.IndexedObjects,
                            CheckoutCompleted: lastCheckoutCompleted,
                            CheckoutTotal: lastCheckoutTotal));
                    }
                    return !ct.IsCancellationRequested;
                },
            },
            OnCheckoutProgress = (path, completedSteps, totalSteps) =>
            {
                lastCheckoutCompleted = completedSteps;
                lastCheckoutTotal = totalSteps;
                progress?.Report(new CloneProgress(
                    BytesReceived: 0,
                    TotalObjects: 0,
                    IndexedObjects: 0,
                    CheckoutCompleted: completedSteps,
                    CheckoutTotal: totalSteps));
            },
        };

        try
        {
            var resultPath = Repository.Clone(cloneUrl, destinationPath, options);
            return new CloneResult.Success(resultPath);
        }
        catch (UserCancelledException)
        {
            TryRemovePartialClone(destinationPath);
            return new CloneResult.Cancelled();
        }
        catch (OperationCanceledException)
        {
            TryRemovePartialClone(destinationPath);
            return new CloneResult.Cancelled();
        }
        catch (LibGit2SharpException ex) when (LooksLikeAuthError(ex))
        {
            TryRemovePartialClone(destinationPath);
            return new CloneResult.AuthFailed(
                "DiffViewer can't clone this repository because authentication " +
                "is required. Clone it locally first (for example with " +
                "`gh repo clone`), then use Browse to point DiffViewer at the " +
                "existing clone.");
        }
        catch (LibGit2SharpException ex)
        {
            TryRemovePartialClone(destinationPath);
            return new CloneResult.Failed($"Clone failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            TryRemovePartialClone(destinationPath);
            return new CloneResult.Failed($"Clone failed: {ex.Message}");
        }
    }

    private static bool LooksLikeAuthError(LibGit2SharpException ex)
    {
        if (ex.Message is null)
        {
            return false;
        }
        var msg = ex.Message;
        return msg.Contains("authentication", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("401", StringComparison.Ordinal)
            || msg.Contains("403", StringComparison.Ordinal)
            || msg.Contains("credentials", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryRemovePartialClone(string destinationPath)
    {
        try
        {
            if (Directory.Exists(destinationPath))
            {
                Directory.Delete(destinationPath, recursive: true);
            }
        }
        catch
        {
            // Best effort. A leftover partial clone is annoying but not
            // dangerous, and a hard failure here would mask the original
            // cancel/auth/clone error from the user.
        }
    }
}
