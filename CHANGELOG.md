# Changelog

All notable changes to DiffViewer are documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
(with the pre-1.0 carve-out: breaking changes ride in minor bumps until v1.0.0).

`.github/workflows/release.yml` reads the section matching the pushed tag
(e.g. `## [0.2.0]` for `v0.2.0`) verbatim and uses it as the GitHub Release
body. Keep section headings exact and write notes in Markdown.

## [Unreleased]

## [1.1.0] - 2026-05-18

### Added

- Loading overlay during context switches. Opening a GitHub PR, a
  different repo, or any other context now shows a window-level
  overlay with an indeterminate progress bar and phased status text
  ("Loading PR #N from owner/repo…" → "Fetching PR metadata…" →
  "Fetching head and merge base…" → "Loading repository…") so the
  10-second-ish gap between dismissing the New Diff dialog and the
  diff appearing is no longer silent. The overlay tracks the same
  switching state that already gated the recents dropdown, so it
  also covers recents-based switches and the missing-clone-then-
  resolve flow.

### Fixed

- F8 / F7 (next / previous change) now navigate relative to where the
  caret actually is, not relative to the last hunk visited via
  keyboard navigation. Previously, after the auto-jump to the first
  hunk on file open, clicking the mouse into a context region between
  later hunks did not update navigation state — so pressing F8 from
  the user's new caret position could step *backwards* to a hunk
  before the caret. F8 now finds the first change after the caret;
  F7 finds the last change before the caret. The overview-bar
  "currently-selected hunk" highlight is unchanged.
- PR fetches no longer fail with a spurious 403 ("GitHub refused the
  request… your token lacks `repo` scope, or your org requires SSO
  authorization"). DiffViewer now sends the `User-Agent` header that
  GitHub's REST API requires; without it, every request was rejected
  regardless of token validity or repo visibility. The 403 error path
  also now includes GitHub's own response-body `message`, so the next
  legitimate 403 (real SSO requirement, IP allowlist, secondary rate
  limit, etc.) self-diagnoses instead of always blaming token scope.
- GitHub PR launches now find the local clone for a repo you already
  have open in DiffViewer, even when its parent directory isn't in
  the Repo roots setting. Previously, switching to a PR for the
  currently-displayed repo could fail with "DiffViewer still can't
  find a local clone of owner/repo" because the locator only
  consulted explicit mappings and configured repo roots. It now
  also probes the recent-contexts MRU list, so the active diff's
  clone (and any clone you've recently opened) is matchable
  without configuration.

## [1.0.0] - 2026-05-18

DiffViewer 1.0 marks the stable cut of the side-by-side Git diff
viewer for Windows. No functional changes since 0.6.0; this release
is the promotion-to-1.0 milestone. From this release onward, breaking
changes to the user-facing surface require a major version bump
(standard SemVer post-1.0).

### What DiffViewer is

A side-by-side / inline Git diff viewer for Windows. Single-file
unsigned `DiffViewer.exe` for Windows 10+ (x64); no .NET install
needed. Compares two `DiffSide` values — `CommitIsh` or `WorkingTree`
— so the supported pairings are working tree vs HEAD, working tree
vs a commit, or commit vs commit. Two derived launch modes layer on
top: **Branch vs merge-base** for "what did this branch add", and
**GitHub PR URL** for one-shot pull-request review. LibGit2Sharp
powers in-process Git access — no `git.exe` shell-out and no auth
plumbing of its own.

### Highlights since 0.1.0

Rolling up the 0.x line (0.1.0 → 0.6.0, 2026-05-14 → 2026-05-18):

- **Launch contexts.** Working-tree-vs-HEAD, working-tree-vs-commit,
  commit-vs-commit, Branch-vs-merge-base, GitHub PR URL. Ref picker
  popup with local + remote-tracking branches, tags, recent refs, and
  an inline merge-base composer. + New Diff button on the recents bar.
- **File list.** Three display modes (full / repo-relative / grouped);
  per-file Viewed checkbox with auto-clear-on-content-change;
  case-insensitive substring filter (`Ctrl+/`); Hide-viewed toggle;
  per-file stage / unstage / revert from the right-click menu; unified
  expand/collapse chrome across modes.
- **Diff pane.** Line-numbers, side-visibility (left / right / both),
  and word-wrap toolbar toggles; per-hunk stage / unstage / revert
  with preview; hunk overview bar; find-in-diff (`Ctrl+F`) with
  case / whole-words / regex.
- **Navigation.** F1 cheat sheet (drift-tested against the actual
  bindings); F7/F8 hunk navigation; F3 / Shift+F3 next / prev match;
  cross-section file-list selection; current-hunk position preserved
  across refreshes; same-file refreshes skip the re-read cycle.
- **Commit metadata.** Per-side header row in the file-list column
  with short SHA / author / relative date / subject; click for a
  modal with full message / absolute date / Copy SHA. Friendly ref
  name badge (`master` / `v0.4.0`) when a ref points exactly at the
  commit, ordered by `git log --decorate` semantics.
- **PR-review feature.** Pass a `github.com` PR URL on the command
  line to resolve the PR's `(merge-base, head)` and launch a normal
  two-commit diff. Auto-detects the local clone (configurable repo
  roots in Settings); prompts to clone if missing. Auth via
  `gh auth token`. Read-only and `github.com`-only.
- **Persistence.** Window size / position / maximized state, splitter
  position, every toolbar toggle, repo roots, remembered clones, and
  recent launch contexts all survive restarts. Drag-release writes
  geometry immediately, so a crash mid-session doesn't lose the most
  recent drag.

### Install

Same story as the 0.x line: single-file unsigned `DiffViewer.exe`
for Windows 10+ (x64). SmartScreen may warn on first launch —
click **"More info"** → **"Run anyway"**. The exe extracts bundled
native libraries to `%TEMP%` on first run.

## [0.6.0] - 2026-05-18

### Added

- **New Diff dialog: ref picker for commit-ish inputs.** The
  "Working tree vs commit" and "Commit vs commit" forms now have a
  **Pick…** button next to each commit-ish field that opens a popup
  listing the repo's local branches, remote-tracking branches, tags,
  and refs you've recently used in this repo. A live case-insensitive
  filter box narrows every group as you type; each row shows the
  ref's friendly name plus its tip's short SHA. The popup also
  includes an inline merge-base composer — fill two refs and click
  **Compute & use** to substitute the resulting SHA into the field —
  for ad-hoc "what did this branch add since it forked" comparisons
  without leaving the dialog. Freeform typing still works as a
  fallback. Enumeration happens off the UI thread so opening the
  picker on a large repo doesn't freeze the dialog. Resolves
  [#4](https://github.com/geevensingh/DiffViewer/issues/4).
- **New Diff dialog: "Branch vs merge-base" mode.** A new top-level
  mode in the left rail wires the dominant PR-style review workflow
  ("what did this branch add since it forked from main") into a
  one-click form. Takes a branch and a merge-base partner, resolves
  their most recent common ancestor on submit, and launches a diff
  with the merge-base on the left and the branch tip on the right —
  so additions land in the right pane the same way every other form
  produces. Surfaces "No common ancestor" inline when the two refs
  have unrelated histories, rather than silently disabling **OK**.
  Both commit-ish inputs get the same ref picker described above.
- **File-list filter + per-file viewed checkbox.** A new bar above
  the display-mode toggle adds a case-insensitive substring filter
  (slash-insensitive: `foo/bar.cs` and `foo\bar.cs` both match) that
  narrows the file list live as you type. Each file row gets a
  GitHub-PR-review-style **Viewed** checkbox on the right; viewed
  rows dim. A toolbar **Hide viewed** toggle composes with the
  filter to suppress reviewed files. Viewed state is per-launch-
  context (in-memory only) and auto-clears when the file's content
  fingerprint (left/right blob SHA, file sizes, status) changes
  between refreshes, so reviewing a stale snapshot doesn't carry
  forward when the file is edited again. Sections and directory
  nodes auto-collapse when none of their descendants are visible.
  Section header chips switch from `(N)` to `(visible / total)`
  while the filter or Hide-viewed is active. Two new keyboard
  shortcuts: `Ctrl+/` focuses the filter, and `Space` (while the
  file list is focused) toggles **Viewed** on the selected row.
  F7/F8 navigation now skips rows hidden by the filter or
  Hide-viewed in addition to whitespace-only ones. The chrome is
  custom-styled to fit the rest of the app: the viewed checkbox is
  invisible at rest and fades in on row hover or selection, uses
  the app's Fluent-blue accent, and sits flush against the row's
  right edge; the filter box gets an italic placeholder and a
  clear-X button; the **Hide viewed** toggle hides itself when
  nothing is marked viewed, and auto-deactivates when the last
  viewed file is unmarked. Selected rows switch text and checkbox
  chrome to white-on-blue so the active row stays legible even
  when its file is viewed-and-dimmed. Resolves
  [#3](https://github.com/geevensingh/DiffViewer/issues/3).

## [0.5.0] - 2026-05-17

### Added

- **Commit metadata in the file-list column.**Each side of a
  comparison that points at a commit now renders a compact header
  row at the top of the file-list column showing the side label,
  short SHA, author name, relative date, and truncated subject.
  Clicking anywhere on the row opens a modal with the full author
  (name plus email), the absolute date in the commit's own timezone,
  both short and full SHA with a Copy-SHA button, and the full
  commit message body in a scrollable region. Working-tree sides
  render no row. For commit-vs-commit comparisons, both sides get
  their own row. When a branch or tag points exactly at the commit,
  a side-tinted ref badge appears before the short SHA in both the
  header row and the dialog — so e.g. comparing against `HEAD` reads
  as `master`, and a tagged release reads as `v0.4.0`. Priority is
  HEAD's branch, then tags, then other local branches, then
  remote-tracking branches; ties within a tier are broken
  alphabetically. Matches `git log --decorate` semantics. Resolves
  [#6](https://github.com/geevensingh/DiffViewer/issues/6).
- **Word-wrap toolbar toggle.** A new **Wrap** button next to the
  **Line #s** toggle wraps long lines at the editor's right edge in
  both side-by-side and inline modes. Useful for files with very long
  lines (minified JS, long Markdown paragraphs, generated configs,
  single-line JSON) where horizontal scrolling becomes the dominant
  reading cost. Toggle state persists across launches alongside the
  other diff-pane settings. Keybinding: `Ctrl+Shift+L`. In
  side-by-side mode each side wraps independently, so paired lines no
  longer align row-for-row when one side has more wrap points than
  the other — this is the cost of seeing the full content in-frame.
  Resolves [#11](https://github.com/geevensingh/DiffViewer/issues/11).

### Fixed

- **Window size and position now persist immediately on drag-release.**
  Previously, dragging the window to a new size or location was only
  persisted on the next Normal↔Maximized state change or on close, so
  a crash or hard kill mid-session lost the most recent drag. The
  window now writes the new geometry as soon as the user releases the
  mouse, matching how the file-list splitter has always behaved.
- **Line-numbers toolbar toggle now persists across launches.** The
  toolbar **Line #s** toggle previously updated the in-memory state
  and the editors but didn't write back to settings, so the choice
  was lost on restart. Caught while wiring up the new word-wrap
  toolbar toggle (issue #11); both toggles now use the same
  persistence path as every other toolbar toggle.

## [0.4.0] - 2026-05-17

### Added

- **F1 opens a keyboard cheat sheet.**A modal dialog lists every
  keyboard shortcut and right-click action, grouped by category
  (View, Navigation, App, Mouse actions). A `?` button on the
  right-hand side of the toolbar opens the same dialog so the
  cheat sheet is discoverable without already knowing the F1
  shortcut. F1 again, Esc, or the Close button dismisses it. A
  drift-detection unit test parses `MainWindow.xaml`'s
  `<Window.InputBindings>` and asserts a bijection with the cheat
  sheet's catalog, so the documentation cannot silently go stale
  relative to the actual key bindings. Resolves
  [#10](https://github.com/geevensingh/DiffViewer/issues/10).

- **Find-in-diff with Ctrl+F.** Press Ctrl+F in the diff pane to
  open AvalonEdit's find bar with Match-Case / Whole-Words / Regex
  toggles. F3 / Shift+F3 step to the next / previous match; Esc
  closes the find bar. Works in both side-by-side and inline modes.
  Ctrl+F pressed while focus is on the file list automatically
  routes to the visible diff editor, so "click file → Ctrl+F" works
  without an intermediate click into the editor. Resolves
  [#2](https://github.com/geevensingh/DiffViewer/issues/2).

- **Launch directly into a GitHub pull request's diff.**Pass a PR URL
  on the command line (`DiffViewer.exe https://github.com/owner/repo/pull/123`)
  and DiffViewer resolves the PR's `(merge-base, head)` into a normal
  two-commit comparison — every existing affordance (side-by-side /
  inline, hunk navigation, stage / unstage / revert, recents) works
  unchanged on PR contexts. Auth is via `gh auth token` (install the
  GitHub CLI and run `gh auth login`); the new **Settings → Repo roots**
  field tells DiffViewer where to look for the matching local clone.
  PRs appear in the recents dropdown labeled `owner/repo#N`; clicking
  one always re-resolves so the latest head SHA is shown — handy after
  a force-push. Read-only and `github.com`-only in v1; see the README
  for the full list of non-goals.

### Fixed

- **Grouped-by-directory mode no longer renders an empty row for
  repo-root files.** When a section contained both a file at the
  repo root and files in subdirectories, the root file used to be
  wrapped in a synthetic empty-label directory node, which showed
  up as a chevron-only header row above the file. Root files now
  sit directly under the section header, at the same indent as the
  root-directory rows.

## [0.3.0] - 2026-05-16

### Added

- **Whole-file Stage / Unstage / Revert** from the file-list
  right-click menu, mirroring the per-hunk stage / unstage / revert
  actions in the diff pane. Eligibility tracks the working-tree
  layer (untracked / unstaged / staged); destructive revert prompts
  with a "Don't ask me again" toggle.
- **File-list / diff-pane splitter position is now persisted across
  launches.** Dragging the splitter used to be a per-session change
  that reset to the default 320 px every time the app launched;
  it now remembers where you left it.

### Changed

- **Toolbar buttons now show clear hover and press feedback in every
  state**, including when a toggle is already "on". Previously the
  "checked" highlight masked the hover highlight, so an active toggle
  looked unresponsive to the cursor. Toggles, the side-visibility
  radio group, and the plain Prev / Next / Settings buttons all share
  the same hover / press chrome now.
- **File-list expand/collapse is now consistent across every display
  mode.** All three modes (Full path, Repo-relative, Grouped by
  directory) render in one unified tree, so section and directory
  rows always use the same triangle chevron — no more mixed
  chevron-vs-Expander chrome between flat and grouped views.
  Clicking anywhere on a section or directory header now toggles
  expand/collapse (matching the affordance the old `Expander` had);
  right-clicking those rows is now a no-op instead of leaving a
  misleading "selected" highlight on a row that doesn't drive any
  state.
- **Refreshing an unchanged file no longer flashes the diff pane.**
  When a repository event (or F5) re-enumerates the change list and
  the currently-open file's content hasn't moved, the diff pane now
  skips the re-read / re-diff / overview-bar redraw cycle entirely
  instead of cycling `IsLoading` and re-rendering identical output.

### Fixed

- **File-list selection across flat and grouped-by-directory modes.**
  Clicking a file in section A, then a file in section B, then
  back to A's previously-selected file used to be a silent no-op
  until a manual refresh — each section's selector held its own
  stale `SelectedItem`. Selection now routes through one source of
  truth so cross-section navigation works on every click.
- **Current-hunk position is preserved across same-file refreshes.**
  A repository refresh used to lose the user's place in the diff
  pane: the file would reload and snap back to the first hunk
  instead of staying on the hunk that was already selected. Hunk
  position now survives the refresh.

### Install

Same story as v0.1.0: single-file unsigned `DiffViewer.exe` for
Windows 10+ (x64). SmartScreen may warn on first launch — click
**"More info"** → **"Run anyway"**.

## [0.2.0] - 2026-05-15

### Added

- **Diff pane toggles** for line numbers (on/off) and side visibility
  (left / right / both), available in both side-by-side and inline modes.
- **Window state persistence.** Window size, position, and maximized
  state are now preserved across launches, so DiffViewer reopens where
  you left it.

### Fixed

- **Long dissimilar paired lines no longer render as yellow-on-both-sides
  with no character-level signal.** When the intra-line similarity check
  rejects a positional pair, both sides now demote to unpaired Deleted /
  Inserted (red/green) so the visual matches the algorithm's "these
  lines aren't really paired" conclusion.
- **Side-by-side "both" view rendering**, inline-mode toggle enablement,
  line-number visibility in inline mode, and hunk-overview-bar
  positioning (now consistently on the right of all editors).
- **Phantom-deleted-file rendering** for Unstaged right-side reads when
  the working-tree blob SHA points at a missing blob.

### Install

Same story as v0.1.0: single-file unsigned `DiffViewer.exe` for
Windows 10+ (x64). SmartScreen may warn on first launch — click
**"More info"** → **"Run anyway"**.

## [0.1.0] - 2026-05-14

Initial public release.

DiffViewer is a WPF 3-way left/right diff viewer for Git repositories on
Windows. This is the first release of the standalone repo — DiffViewer
was extracted from geevensingh/DevTools with its full commit history
(`git filter-repo`) so every prior bug fix and feature is preserved
here.

### Added

- Single-file `DiffViewer.exe` for Windows 10+ (x64). No .NET install
  needed.
- Side-by-side diff between two commits or working-tree vs HEAD.
- F7 / F8 (or toolbar **Prev** / **Next**) to walk hunks.
- Per-hunk revert with a one-line summary preview.
- Recent launch contexts dropdown for fast re-launch.

### Install

The exe extracts bundled native libraries to `%TEMP%` on first run; some
AV products briefly scan during this step. Subsequent launches are
fast.

The binary is unsigned, so Windows SmartScreen may warn on first
launch — click **"More info"** → **"Run anyway"**. Code signing is
planned for a later release.

[Unreleased]: https://github.com/geevensingh/DiffViewer/compare/v0.6.0...HEAD
[0.6.0]: https://github.com/geevensingh/DiffViewer/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/geevensingh/DiffViewer/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/geevensingh/DiffViewer/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/geevensingh/DiffViewer/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/geevensingh/DiffViewer/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/geevensingh/DiffViewer/releases/tag/v0.1.0
