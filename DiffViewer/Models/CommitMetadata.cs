namespace DiffViewer.Models;

/// <summary>
/// Resolved commit metadata for one side of a comparison whose
/// <see cref="DiffSide"/> is a <see cref="DiffSide.CommitIsh"/>. Captured
/// at the time the comparison view-model is built; never re-fetched.
///
/// <para>Working-tree sides do not have <see cref="CommitMetadata"/> —
/// the panel view-model for that side stays in its "no commit" branch.</para>
/// </summary>
/// <param name="Sha">Full 40-char SHA.</param>
/// <param name="ShortSha">Display-friendly truncation. Flat 7 chars (or the
/// whole SHA if shorter — only happens in pathological test fixtures); a
/// future iteration can fold in git's collision-aware short-sha logic, but
/// flat 7 matches the issue's wording and avoids another LibGit2Sharp
/// round-trip per commit.</param>
/// <param name="AuthorName">Author's display name from the commit signature.</param>
/// <param name="AuthorEmail">Author's email from the commit signature.</param>
/// <param name="AuthorDate">Author timestamp. <see cref="DateTimeOffset"/>
/// preserves the commit's own timezone so the "absolute on hover" tooltip
/// can render the commit's local time rather than the viewer's.</param>
/// <param name="MessageSubject">First paragraph of the commit message
/// (libgit2's <c>MessageShort</c> — multi-line subjects are folded to
/// one line with spaces).</param>
/// <param name="MessageBody">Everything after the first blank line in the
/// commit message; empty string when the message is subject-only.</param>
public sealed record CommitMetadata(
    string Sha,
    string ShortSha,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthorDate,
    string MessageSubject,
    string MessageBody);
