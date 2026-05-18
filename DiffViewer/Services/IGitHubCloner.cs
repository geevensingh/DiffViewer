namespace DiffViewer.Services;

/// <summary>
/// Seam over <see cref="LibGit2Sharp.Repository.Clone(string, string, LibGit2Sharp.CloneOptions)"/>
/// so the <c>MissingClonePromptViewModel</c> can be tested without a network or
/// disk. Production: <see cref="LibGit2GitHubCloner"/>.
/// </summary>
/// <remarks>
/// <para>The interface intentionally returns a discriminated <see cref="CloneResult"/>
/// instead of throwing for auth failure: the v1 UX distinguishes
/// "auth failed → tell the user to clone with <c>gh repo clone</c> and
/// come back via Browse" from "generic clone failure → show the error and
/// let them retry". A typed result keeps that branch out of message-string
/// pattern matching.</para>
///
/// <para>Cancellation is honored at LibGit2Sharp's transfer-progress and
/// checkout-progress callbacks. On cancellation, the partial-clone
/// directory must be removed before <see cref="CloneResult.Cancelled"/>
/// is returned — half-cloned trees are otherwise indistinguishable from
/// valid clones on the next pass.</para>
/// </remarks>
public interface IGitHubCloner
{
    Task<CloneResult> CloneAsync(
        string cloneUrl,
        string destinationPath,
        IProgress<CloneProgress>? progress,
        CancellationToken ct);
}

/// <summary>
/// Progress snapshot from a running clone. Both transfer and checkout
/// counters are exposed because LibGit2Sharp emits them separately and
/// the UI's overall progress bar combines them.
/// </summary>
public sealed record CloneProgress(
    int BytesReceived,
    int TotalObjects,
    int IndexedObjects,
    int CheckoutCompleted,
    int CheckoutTotal);

/// <summary>Outcome of a <see cref="IGitHubCloner.CloneAsync"/> call.</summary>
public abstract record CloneResult
{
    /// <summary>Clone completed and <see cref="ClonePath"/> is a usable working copy.</summary>
    public sealed record Success(string ClonePath) : CloneResult;

    /// <summary>
    /// Authentication failed. The v1 UX directs users to clone the repo
    /// locally with their own credentials (<c>gh repo clone</c>) and come
    /// back to DiffViewer via the Browse-to-existing-clone path.
    /// </summary>
    public sealed record AuthFailed(string Message) : CloneResult;

    /// <summary>
    /// Clone failed for a reason other than auth: network, disk, ref-not-found,
    /// etc. <see cref="Message"/> is user-displayable.
    /// </summary>
    public sealed record Failed(string Message) : CloneResult;

    /// <summary>
    /// User cancelled. Implementations must remove the partial clone
    /// directory before returning this.
    /// </summary>
    public sealed record Cancelled : CloneResult;
}
