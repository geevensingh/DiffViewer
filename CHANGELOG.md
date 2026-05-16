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
