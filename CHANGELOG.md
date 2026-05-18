# Changelog

All notable changes to DiffViewer are documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
(with the pre-1.0 carve-out: breaking changes ride in minor bumps until v1.0.0).

`.github/workflows/release.yml` reads the section matching the pushed tag
(e.g. `## [0.2.0]` for `v0.2.0`) verbatim and uses it as the GitHub Release
body. Keep section headings exact and write notes in Markdown.

## [Unreleased]

### Added

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

[Unreleased]: https://github.com/geevensingh/DiffViewer/compare/v0.5.0...HEAD
[0.5.0]: https://github.com/geevensingh/DiffViewer/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/geevensingh/DiffViewer/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/geevensingh/DiffViewer/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/geevensingh/DiffViewer/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/geevensingh/DiffViewer/releases/tag/v0.1.0
