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
- File list grouped by working-tree layer (Conflicted, Committed since
  baseline, Staged, Unstaged, Untracked), with three presentation modes —
  full path, repo-relative, or grouped-by-directory — and a status badge
  on each row.
- Hunk navigation by F7 (previous) / F8 (next), Shift+F7/F8 to step by
  file, Ctrl+F7/F8 to step by section, or Alt+Up/Down as screen-reader
  aliases. Toolbar Prev / Next buttons fire the same commands.
- **Stage hunk**, **unstage hunk**, and **revert hunk** from the
  right-click menu on a diff line. Revert prompts with a "Discard this
  hunk from the working tree" confirmation (suppressible).
- Live updates when the working tree changes (Ctrl+L; automatically
  disabled for commit-vs-commit comparisons).
- Persistent settings and a **recent launch contexts** dropdown (up to 10
  entries) so jumping back to "working tree of repo X" or "two commits in
  repo Y" is one click.
- Self-contained Windows x64 distribution — no .NET runtime install needed.

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
