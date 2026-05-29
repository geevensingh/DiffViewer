# Changelog

All notable changes to DiffViewer are documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
(with the pre-1.0 carve-out: breaking changes ride in minor bumps until v1.0.0).

`.github/workflows/release.yml` reads the section matching the pushed tag
(e.g. `## [0.2.0]` for `v0.2.0`) verbatim and uses it as the GitHub Release
body. Keep section headings exact and write notes in Markdown.

## [Unreleased]

### Fixed

- **UTF-16 / UTF-32 source files no longer show "Binary file - diff
  not displayed."** The binary-detection heuristic — git's "any NUL
  byte in the first 8 KiB" rule — used to fire on UTF-16-encoded text
  files because every ASCII character is paired with a NUL high byte.
  The detector now exempts buffers that start with a recognised text
  BOM (UTF-8, UTF-16 LE/BE, UTF-32 LE/BE), and the same exemption is
  applied to LibGit2Sharp's blob-side `IsBinary` check so committed
  UTF-16 blobs decode correctly too. The downstream encoding
  detector already handled all five BOM forms; only the binary
  short-circuit needed teaching.

### Added

- **SVG diff rendering (text + rasterized image).** `.svg` files now
  decode through a new SharpVectors-backed image decoder and render in
  the existing image-diff pane alongside PNG / JPEG / GIF / BMP / ICO,
  using the same Side-by-side / Swipe / Onion-skin modes. A new
  **Rendered** toolbar toggle (visible only on `.svg` files where at
  least one side rasterised successfully) flips between the rendered
  bitmap view and the underlying XML text diff; the preference is
  persisted per `AppSettings.RenderSvgImage`. SVGs that fail to parse
  fall back to the XML text view rather than the binary-blob
  placeholder, and the Rendered toggle auto-hides so users aren't
  offered a button that does nothing. Rasterisation runs at up to a
  1024 px max-dimension (aspect preserved), letting WPF down-sample
  for fit-to-frame without re-rasterising on resize. Resolves #15.

## [1.3.0] - 2026-05-20

### Added

- **Stash support in the ref-picker popup.** The "Pick…" popup next
  to every commit-ish input now includes a *Stashes* section showing
  all stash entries (symbolic name, subject, short SHA) ordered
  most-recent-first. Picking a stash writes `stash@{N}` into the
  commit-ish textbox. Filter matches against the symbolic name,
  subject text, and short SHA.
- **"View stash" mode in the New Diff dialog.** A dedicated mode
  that shows an inline stash list and launches a comparison of the
  stash's working-tree snapshot against HEAD at stash time
  (`stash@{N}^1` → `stash@{N}`), matching `git stash show`
  semantics. Empty repos or repos with no stashes show a clear
  empty-state message.
- **New Diff dialog auto-detects a GitHub PR URL on the clipboard.**
  Opening *New diff* while a GitHub PR URL is on the clipboard now
  switches the dialog to *GitHub pull request* mode and pre-fills the
  URL field — so the common "I just copied a PR link from Outlook /
  chat / GitHub" path collapses to one click on *OK*. URLs with
  trailing `/files`, `/commits/<sha>`, query strings, or fragments
  are tolerated (same parser as the CLI). The detection is
  per-open: your session's last-used mode is not overwritten, so
  re-opening the dialog without a PR URL on the clipboard reverts
  to whatever mode you were actually working in. Junk on the
  clipboard, a clipboard owned by another app, or a clipboard
  containing a non-PR URL are all silently ignored.
- **New Diff dialog: *Branch vs merge-base* auto-seeds the partner
  field with `origin/<default-branch>`.** When the form's repo
  path resolves and the partner field is empty, the dialog now
  reads `refs/remotes/origin/HEAD` and pre-fills the partner with
  the friendly remote-tracking name (e.g. `origin/main` or
  `origin/master`). Cuts the dominant PR-review interaction to
  "type a branch name and hit OK". Clones without an
  `origin/HEAD` symref (some clones never set one), repos without
  an `origin` remote, and invalid repo paths leave the field
  empty — no surprise.
- **New Diff dialog: cold-launch seeds the repo path from your
  most-recent diff.** When you open *New diff* without an active
  context (e.g. straight from the recent-contexts list with no
  context currently open), every local-provider form now starts
  with the repo field pre-filled from the top of the recent-launch
  MRU. Empty MRU + no active context leaves the field blank.

### Fixed

- **Annotated tags now resolve everywhere a commit-ish is accepted.**
  Picking an annotated tag (e.g. `v0.2.0` created with `git tag -a`)
  in the New Diff dialog used to fail with *"Cannot resolve
  `v0.2.0`"* — first at validation, and after that was fixed, again
  at diff enumeration. Root cause was the same in both places:
  LibGit2Sharp's generic `Lookup<Commit>` returns null for an
  annotated tag because the tag's ref points at a `TagAnnotation`
  wrapper, not the commit itself, so callers that didn't peel
  through the wrapper saw the tag as unknown. The ref-picker,
  validator, and diff-engine internals now share a single
  `CommitIshResolver.PeelToCommit` helper that handles both
  lightweight and annotated tags. The same code path serves the
  CLI, so passing an annotated tag like `DiffViewer.exe <repo>
  v0.2.0 origin/master` also works.
- **New Diff dialog: `Pick…` ref-picker buttons now actually open.**
  The buttons next to the commit-ish fields in the *Working tree vs
  commit*, *Commit vs commit*, and *Branch vs merge-base* forms were
  stuck disabled until you also typed a commit-ish — defeating the
  point of the picker. They now enable as soon as the repo path
  resolves, including when the dialog opens with a prefilled repo
  path from the active context. Under the hood, the form view-models
  no longer canonicalise the repo path as a side effect of
  `ComputeValidationError` (which short-circuited on empty
  commit-ish fields), so the picker's `CanonicalRepoPath` is in sync
  with the user's repo-path input on every keystroke. No behaviour
  change to the OK button or the deferred error-message UX:
  validation errors still suppress until every required field is
  populated.

## [1.2.0] - 2026-05-19

### Added

- **Command-line flags for direct launch**: `DiffViewer.exe --repo
  <path> --left <commit-ish | WORKING> --right <commit-ish | WORKING>
  [--file <repo-relative-path>]` launches straight into a diff context,
  bypassing the launch-context picker. `WORKING` is the sentinel for
  "working tree" (matches `DiffSide.WorkingTree`). `--file` pre-selects
  a file in the file list, with case-insensitive matching and both
  forward- and backward-slash separators accepted. Errors (missing or
  unknown flag, missing flag value, unresolvable ref, repo not found)
  print to stderr and exit with status 1; when DiffViewer is launched
  from a terminal those messages appear inline in that terminal, when
  launched from Explorer they fall back to the existing error dialog.
  The existing positional grammar (`DiffViewer.exe <path>`,
  `DiffViewer.exe <pr-url>`, ...) is unchanged. See the new
  "Command-line launch" section in the README for the `git` alias
  recipe.
- Hunk overview bar is now interactive for scrolling. Drag the
  viewport indicator (the soft blue outline that shows the editors'
  currently-visible window) up or down to scroll both editors —
  sticky-thumb behaviour, so whichever part of the indicator you
  grab stays under the cursor for the whole drag. Drag scrolling is
  pixel-precise (each pixel of mouse movement produces a sub-line
  of editor scroll) so fine adjustments work even when one bar pixel
  spans many file lines. The scroll wheel also works anywhere over
  the bar and moves the editors at the same rate as wheeling the
  editor surface itself (one notch =
  `SystemParameters.WheelScrollLines` lines). Neither interaction
  moves the caret or selects a hunk — they're pure scroll, leaving
  the editor caret and the active-hunk highlight unchanged. Clicking
  a hunk marker still jumps to that hunk; that behaviour is
  unchanged. Click-to-jump commits on mouse-up (not mouse-down) so
  that a press landing on a hunk marker which overlaps the viewport
  band can still promote to a drag if the user moves the mouse past
  the system drag threshold — previously, the jump fired immediately
  and stole the drag. The viewport indicator paints at the cursor's
  position on every mouse-move frame instead of waiting for the
  editor's scroll + layout pass to round-trip — keeps the indicator
  truly stuck to the cursor even on large files where the editor
  scroll can't quite keep up at fast drag speeds. Clicking empty
  bar space (anywhere that isn't a hunk marker or the viewport
  indicator) recenters the indicator on the cursor and starts a
  drag in the same gesture — minimap-style "click anywhere to jump
  there", and a press-and-drag from empty space scrubs without
  needing a second click. The indicator lands at the editor's
  natural settle position for the click — in side-by-side mode that
  always equals the cursor position, in inline mode it can be
  slightly offset because the inline document interleaves
  deletion lines (which advance the editor's scroll but not the
  right-side line count); picking the post-settle position up
  front keeps the indicator stable across MouseUp instead of
  visibly jumping when the editor's viewport catches up.
- Language-aware syntax highlighting for TypeScript (`.ts`, `.tsx`),
  YAML (`.yaml`, `.yml`), Go (`.go`), Rust (`.rs`), Ruby (`.rb`),
  Bash / shell (`.sh`, `.bash`, `.zsh`), and TOML (`.toml`). Combined
  with AvalonEdit's bundled definitions (C#, JavaScript, JSON, XML,
  HTML, CSS, Python, PowerShell, T-SQL, Java, C/C++, PHP, VB, etc.)
  this covers the most common file types in modern repositories.
  Highlighting is regex-based and intentionally conservative — diff
  background and intra-line word coloring still take precedence over
  language coloring, so changed-line signal isn't masked. Some
  pathological constructs may mis-color (notably TypeScript JSX
  attributes and Ruby heredocs); patch reports welcome.
- Image diff for binary image blobs. PNG, JPEG, GIF, BMP, and ICO
  files now render as an actual image-diff view instead of the
  generic "Binary file - diff not displayed." placeholder. Three
  viewing modes are exposed via toolbar radio buttons that replace
  the text-only toggles when an image is shown: **Side-by-side**
  (two fit-to-frame panes on a checkerboard background), **Swipe**
  (overlay both images with a draggable divider — drag anywhere
  over the canvas), and **Onion-skin** (stack both images with an
  opacity slider to blend between them). For added/deleted files
  the single available image fills the full canvas and the mode
  controls are hidden, since the three modes have no meaningful
  semantics when one side is absent. ICO files render the largest
  embedded resolution (a 256x256 frame is preferred over the 16x16
  frame when both are present). A header strip shows each side's
  dimensions, byte size, and the byte delta (e.g.
  `512 x 512 / 18.3 KB -> 1024 x 1024 / 64.1 KB  (+45.8 KB)`),
  with an `(animated, first frame)` suffix when either side is a
  multi-frame GIF. The size gate reuses the existing
  `LargeFileThresholdBytes` setting; over-threshold images still
  fall back to the binary placeholder. Format and mode state is
  per-file in memory, not persisted in `AppSettings`. SVG support
  is tracked separately as issue #15.

## [1.1.1] - 2026-05-18

### Changed

- CI/release pipeline now runs on Node.js 24-compatible GitHub
  Actions (`actions/checkout@v6`, `actions/setup-dotnet@v5`,
  `softprops/action-gh-release@v3`), ahead of the June 2 / Sep 16
  2026 Node.js 20 deprecation deadlines. No user-visible behavior
  change; the shipped `DiffViewer.exe` is functionally identical to
  v1.1.0.

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
