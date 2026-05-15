# DiffViewer

A WPF-based 3-way left/right diff viewer for Git repositories. Compare a
working tree against HEAD, compare two arbitrary commits, jump between hunks
with keyboard shortcuts, and revert individual hunks back to the index.

## Features

- Side-by-side diff view across two commits (or working tree vs HEAD) for any
  Git repository.
- File list panel with status-aware sorting (added, modified, renamed,
  deleted).
- Per-hunk navigation: F7 (previous) / F8 (next), or the toolbar Prev / Next
  buttons.
- Hunk-level revert with a one-line preview confirming what will change.
- Persistent settings and a **recent launch contexts** dropdown so jumping
  back to "working tree of repo X" or "two commits in repo Y" is one click.
- Self-contained Windows x64 distribution — no .NET runtime install needed.

## Requirements

- Windows 10 or later, **x64**. Windows on ARM users need x64 emulation or a
  from-source build until an arm64 release is published.
- ~150 MB of disk space (the bundled .NET 8 runtime + LibGit2Sharp native
  libraries make the single-file exe large).

### First launch

The single-file exe extracts bundled native libraries to `%TEMP%` on first
launch. Some AV products briefly scan during this step, so the very first run
may take a few extra seconds. Subsequent launches are fast.

## Install

1. Go to the [Releases page] and download `DiffViewer.exe` from the latest
   release.
2. Double-click to launch. Windows SmartScreen may show a warning for the
   unsigned binary; click **More info** → **Run anyway**. Code signing is on
   the roadmap.

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

Tag-driven. Push a `vX.Y.Z` tag and a GitHub Actions workflow builds the
release exe and publishes it to the Releases page. Tag annotations are used
as the release notes body — write a meaningful message with `git tag -a`.

## History

DiffViewer started life inside the [geevensingh/DevTools][devtools] personal
toolkit monorepo and was extracted into its own repo so releases could be
distributed without mixing in unrelated tools' version history. The full
commit history from DevTools was preserved via `git filter-repo`.

## License

[MIT](LICENSE).

[Releases page]: https://github.com/geevensingh/DiffViewer/releases
[devtools]: https://github.com/geevensingh/DevTools
