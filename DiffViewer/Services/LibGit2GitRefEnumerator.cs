using System;
using System.Collections.Generic;
using System.Linq;
using LibGit2Sharp;

namespace DiffViewer.Services;

/// <summary>
/// LibGit2Sharp-backed <see cref="IGitRefEnumerator"/>. Opens, reads,
/// closes — one short-lived <see cref="Repository"/> handle per call.
/// Safe to construct as an app-level singleton (no mutable state).
///
/// <para><b>Remote-tracking filter</b>: the synthetic
/// <c>origin/HEAD</c> symbolic ref is excluded from the remote-branch
/// list because it just mirrors whatever the picker already shows
/// under its real name (e.g. <c>origin/master</c>). Including it
/// would create a confusing duplicate row.</para>
///
/// <para><b>Tag handling</b>: both lightweight and annotated tags
/// are returned. <see cref="Tag.Target"/>.<see cref="GitObject.Peel{T}"/>
/// resolves annotated tags through their wrapper object to the
/// underlying commit. Tags that don't peel to a commit (e.g. tag of
/// a blob — rare) are dropped silently.</para>
/// </summary>
public sealed class LibGit2GitRefEnumerator : IGitRefEnumerator
{
    public RefEnumerationResult Enumerate(string canonicalRepoPath)
    {
        if (string.IsNullOrWhiteSpace(canonicalRepoPath)) return RefEnumerationResult.Empty;
        if (!Repository.IsValid(canonicalRepoPath)) return RefEnumerationResult.Empty;

        try
        {
            using var repo = new Repository(canonicalRepoPath);

            var local = repo.Branches
                .Where(b => !b.IsRemote && b.Tip is not null)
                .Select(b => new RefEntry(b.FriendlyName, b.Tip!.Sha, ShortSha(b.Tip.Sha)))
                .OrderBy(e => e.FriendlyName, StringComparer.Ordinal)
                .ToArray();

            var remote = repo.Branches
                .Where(b => b.IsRemote
                            && b.Tip is not null
                            // origin/HEAD is a symbolic ref that mirrors
                            // origin/master (or whatever the default branch
                            // is). Showing both is noise.
                            && !b.FriendlyName.EndsWith("/HEAD", StringComparison.Ordinal))
                .Select(b => new RefEntry(b.FriendlyName, b.Tip!.Sha, ShortSha(b.Tip.Sha)))
                .OrderBy(e => e.FriendlyName, StringComparer.Ordinal)
                .ToArray();

            var tags = repo.Tags
                .Select(t => new
                {
                    Name = t.FriendlyName,
                    Commit = t.Target?.Peel<Commit>(),
                })
                .Where(x => x.Commit is not null)
                .Select(x => new RefEntry(x.Name, x.Commit!.Sha, ShortSha(x.Commit.Sha)))
                .OrderBy(e => e.FriendlyName, StringComparer.Ordinal)
                .ToArray();

            return new RefEnumerationResult(local, remote, tags);
        }
        catch (LibGit2SharpException)
        {
            return RefEnumerationResult.Empty;
        }
        catch (Exception)
        {
            return RefEnumerationResult.Empty;
        }
    }

    public string? TryComputeMergeBase(string canonicalRepoPath, string refA, string refB)
    {
        if (string.IsNullOrWhiteSpace(canonicalRepoPath)) return null;
        if (string.IsNullOrWhiteSpace(refA)) return null;
        if (string.IsNullOrWhiteSpace(refB)) return null;
        if (!Repository.IsValid(canonicalRepoPath)) return null;

        try
        {
            using var repo = new Repository(canonicalRepoPath);
            var commitA = repo.Lookup<Commit>(refA);
            var commitB = repo.Lookup<Commit>(refB);
            if (commitA is null || commitB is null) return null;

            // FindMergeBase returns null when the two histories share no
            // common ancestor (orphaned branches). Surface that null so
            // the caller can render a "no common ancestor" hint instead
            // of a misleading "merge-base of X" SHA.
            return repo.ObjectDatabase.FindMergeBase(commitA, commitB)?.Sha;
        }
        catch (LibGit2SharpException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string? TryGetDefaultRemoteBranch(string canonicalRepoPath)
    {
        if (string.IsNullOrWhiteSpace(canonicalRepoPath)) return null;
        if (!Repository.IsValid(canonicalRepoPath)) return null;

        try
        {
            using var repo = new Repository(canonicalRepoPath);
            // refs/remotes/origin/HEAD is the symbolic ref `git clone`
            // installs to record what the remote's HEAD was at clone
            // time. Not every clone has it (older Gits, manually
            // configured remotes, --no-tags variants) — in which case
            // we return null and let the caller leave the partner
            // field blank.
            var headRef = repo.Refs["refs/remotes/origin/HEAD"];
            if (headRef is not SymbolicReference symref) return null;

            var target = symref.Target;
            if (target is null) return null;

            const string remotePrefix = "refs/remotes/";
            var canonical = target.CanonicalName;
            if (string.IsNullOrEmpty(canonical)
                || !canonical.StartsWith(remotePrefix, StringComparison.Ordinal))
            {
                return null;
            }

            var friendly = canonical[remotePrefix.Length..];
            // Defensive: ignore an origin/HEAD that points back at itself
            // (would produce a nonsense recursive seed). Real-world this
            // doesn't happen — libgit2's git clone produces e.g.
            // refs/remotes/origin/HEAD -> refs/remotes/origin/main.
            if (friendly.Length == 0
                || friendly.EndsWith("/HEAD", StringComparison.Ordinal))
            {
                return null;
            }
            return friendly;
        }
        catch (LibGit2SharpException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ShortSha(string sha) => sha.Length >= 7 ? sha[..7] : sha;
}
