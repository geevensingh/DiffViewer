# DiffViewer

A WPF-based side-by-side Git diff viewer for Windows. Compare a working
tree against HEAD or any commit, compare two arbitrary commits, jump
between hunks with keyboard shortcuts, and stage / unstage / revert
individual hunks.

## Features

- **Side-by-side and inline** diff views (toggle with Ctrl+I), with optional
  word-level intra-line highlights (Ctrl+D), ignore-whitespace (Ctrl+W),
  and show-whitespace (Ctrl+Shift+W) toggles.
- Three launch contexts: **working tree vs HEAD**, **working tree vs any
  commit**, or **two arbitrary commits**.
- **Review a GitHub pull request** by passing its URL on the command line
  (e.g. `DiffViewer.exe https://github.com/owner/repo/pull/123`). See
  [Pull request review](#pull-request-review) below for setup.
- File list grouped by working-tree layer (Conflicted, Committed since
  baseline, Staged, Unstaged, Untracked), with three presentation modes —
  full path, repo-relative, or grouped-by-directory — and a status badge
  on each row.
- Hunk navigation by F7 (previous) / F8 (next), Shift+F7/F8 to step by
  file, Ctrl+F7/F8 to step by section, or Alt+Up/Down as screen-reader
  aliases. Toolbar Prev / Next buttons fire the same commands.
- **F1 (or the toolbar `?` button) opens a keyboard cheat sheet**
  listing every shortcut and right-click action, grouped by category
  (View, Navigation, App, Mouse actions). F1 again, Esc, or the
  Close button dismisses it.
- **Stage hunk**, **unstage hunk**, and **revert hunk** from the
  right-click menu on a diff line. Revert prompts with a "Discard this
  hunk from the working tree" confirmation (suppressible).
- Live updates when the working tree changes (Ctrl+L; automatically
  disabled for commit-vs-commit comparisons).
- Persistent settings and a **recent launch contexts** dropdown (up to 10
  entries) so jumping back to "working tree of repo X" or "two commits in
  repo Y" is one click.
- Self-contained Windows x64 distribution — no .NET runtime install needed.

## Pull request review

DiffViewer can launch directly into a GitHub pull request's diff:

```text
DiffViewer.exe https://github.com/owner/repo/pull/123
```

The PR's `(merge-base, head)` is resolved into the same kind of two-commit
diff the rest of the app already renders, so every existing affordance —
side-by-side / inline, hunk navigation, stage / unstage / revert,
recents — works on PRs without any new UI to learn.

### One-time setup

1. **Install the [GitHub CLI][gh-cli] and run `gh auth login`.** DiffViewer
   asks `gh` for an OAuth token via `gh auth token` (per-host) on every PR
   load, so the token in DiffViewer always matches the token in your
   terminal. PATs in settings are not supported in v1.
2. **Tell DiffViewer where your clones live.** Open **Settings → Repo
   roots** and add one or more directories whose immediate children are
   git clones (e.g. `C:\Repos`). On a PR launch, DiffViewer scans every
   remote of every clone under those roots — not just `origin` — to find
   one whose remote URL matches the PR's `owner/repo`. SSH and HTTPS URL
   forms are both recognized and matching is case-insensitive.

### Missing-clone dialog

If no matching local clone is found, DiffViewer offers two options:

- **Browse to an existing clone** somewhere else on disk. DiffViewer
  validates that one of its remotes matches the PR's repo (with a
  confirmation when none do — useful when you've renamed remotes), then
  remembers the mapping so the next PR from that repo skips this dialog.
  **This is also how you review PRs against private repos in v1**: clone
  once with `gh repo clone` (where your existing credentials work), then
  point DiffViewer at the path.
- **Clone for me** into a destination you pick. Public repos only in v1;
  private clones go through Browse.

### Refs DiffViewer writes into your clone

To diff a PR head against its base, DiffViewer fetches both into the
local clone under its own refs namespace:

- `refs/diffviewer/pr/N/head` — the PR's head commit.
- `refs/diffviewer/base/<branch>` — the PR's base branch tip.

These are local-only (never pushed) and accumulate over time as you view
PRs. They show up in `git for-each-ref` and are otherwise harmless;
v1 doesn't include a cleanup action.

### Non-goals (v1)

DiffViewer's PR review is a **read-only**, **public-repo-clone-friendly**,
`github.com`-only first cut. The following are deliberate omissions:

- No posting review comments, drafts, or approve / request-changes
  submissions.
- No inline rendering of existing review threads on the diff.
- No "mark file as viewed" state.
- No GitHub Enterprise Server (host plumbing is in place; configuration
  is a follow-up).
- No PAT-in-settings auth — `gh auth login` is the only path in v1.
- No "Clone for me" against private repos — use Browse instead.
- No per-commit diffs inside a PR (only the cumulative 3-dot view).
- No PR metadata (title, author, state) in the window chrome beyond
  what shows up in the recents dropdown.

[gh-cli]: https://cli.github.com/

## Requirements

- Windows 10 or later, **x64**. Windows on ARM users need x64 emulation or a
  from-source build until an arm64 release is published.
- ~145 MB on disk for `DiffViewer.exe`, plus a few extra MB extracted to
  `%TEMP%` on first run (the bundled .NET 8 runtime and native Git
  libraries make the single-file exe large).

### First launch

The single-file exe extracts bundled native libraries to `%TEMP%` on first
launch. Some AV products briefly scan during this step, so the very first run
may take a few extra seconds. Subsequent launches are fast.

## Install

1. Go to the [Releases page] and download `DiffViewer.exe` from the latest
   release.
2. Double-click to launch. Windows SmartScreen may show a warning for the
   unsigned binary; click **More info** → **Run anyway**. The release exe
   is not currently code-signed.

## Build from source

Requires the .NET 8 SDK or newer.

- Day-to-day development: `dotnet build -c Release`
- Run the test suite: `dotnet test`
- Produce a release-equivalent single-file exe locally:
  `dotnet publish DiffViewer\DiffViewer.csproj -c Release -o publish`
  (matches what the release workflow does on tag push)

The publish settings (single-file, self-contained, native-libraries-bundled)
live in `DiffViewer/DiffViewer.csproj`.

## Releases

Tag-driven. Push an **annotated** `vX.Y.Z` tag and a GitHub Actions
workflow builds the release exe and publishes it to the Releases page.
The tag annotation is used verbatim as the release notes body, so always
create the tag with `git tag -a` and write a meaningful message.

## History

DiffViewer started life inside the [geevensingh/DevTools][devtools] personal
toolkit monorepo and was extracted into its own repo so releases could be
distributed without mixing in unrelated tools' version history. The full
commit history from DevTools was preserved via `git filter-repo`.

## License

[MIT](LICENSE).

[Releases page]: https://github.com/geevensingh/DiffViewer/releases
[devtools]: https://github.com/geevensingh/DevTools
