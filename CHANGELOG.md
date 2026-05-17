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

- **Whole-file Stage / Unstage / Revert** from the file-list
  right-click menu, mirroring the per-hunk stage / unstage / revert
  actions in the diff pane. Eligibility tracks the working-tree
  layer (untracked / unstaged / staged); destructive revert prompts
  with a "Don't ask me again" toggle.

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

### Fixed

- **Crash when clicking a section or directory header label.** The
  expand-on-click handler walked the visual tree from the click's
  original source; when the click landed on text inside the header
  label, that source was a `Run` content element rather than a
  `Visual`, which threw `InvalidOperationException` and tore down
  the app. The walk now handles both visual and logical ancestors.
- **Clicking a file row collapsed its parent section, and right-
  clicking a file row swallowed its context menu.** The section /
  directory header handlers fired for any mouse event in their
  subtree — including events that originated inside a descendant
  row — because the bubbling/tunneling routing reaches the parent
  on its way through the tree. Both handlers now bail when the
  click started inside a different `TreeViewItem`, so file-row
  clicks select the file and show the entry context menu as
  expected, and nested-directory clicks no longer affect their
  containing section or directory.

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

[Unreleased]: https://github.com/geevensingh/DiffViewer/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/geevensingh/DiffViewer/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/geevensingh/DiffViewer/releases/tag/v0.1.0
