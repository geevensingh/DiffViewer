using System;
using System.Collections.Generic;
using DiffViewer.Services;

namespace DiffViewer.Tests;

/// <summary>
/// Test double for <see cref="IGitWorktreeEnumerator"/>. Returns a
/// caller-supplied list regardless of the path, or nothing by default —
/// which is what most form tests want, since they exercise validation
/// and launch-source construction rather than worktree discovery.
/// </summary>
internal sealed class StubWorktreeEnumerator : IGitWorktreeEnumerator
{
    private readonly IReadOnlyList<WorktreeEntry> _entries;

    public StubWorktreeEnumerator(params WorktreeEntry[] entries)
    {
        _entries = entries ?? Array.Empty<WorktreeEntry>();
    }

    /// <summary>Paths this stub was asked about, in call order.</summary>
    public List<string> EnumeratedPaths { get; } = new();

    public IReadOnlyList<WorktreeEntry> Enumerate(string canonicalRepoPath)
    {
        EnumeratedPaths.Add(canonicalRepoPath);
        return _entries;
    }
}
