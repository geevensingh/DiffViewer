# Contributing to DiffViewer

Thanks for your interest in contributing. This is a small, single-maintainer
project, but outside contributions are welcome.

## Tech stack at a glance

- **.NET 8** targeting `net8.0-windows` (WPF).
- **[AvalonEdit](https://github.com/icsharpcode/AvalonEdit)** for the diff
  text editors (left, right, and inline panes).
- **[DiffPlex](https://github.com/mmanela/diffplex)** for the line-level
  and word-level diff algorithm.
- **[LibGit2Sharp](https://github.com/libgit2/libgit2sharp)** for reading
  repository state. Hunk write operations (stage / unstage / revert) shell
  out to `git.exe` on `PATH` via `GitWriteService`.
- **[CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)**
  for `ObservableObject`, `[RelayCommand]`, and `[ObservableProperty]`.
- **xUnit** + **FluentAssertions** for the test project.

## Project layout

- `DiffViewer/` — the WPF app.
- `DiffViewer.Tests/` — xUnit unit tests, mostly targeting view-models and
  services. `InternalsVisibleTo` is granted to the test project so
  `internal` helpers are reachable.

`AGENTS.md` at the repo root is an agent-facing structured reference that
also serves as a quick map of the codebase — read it for the short version
of project conventions.

## Build, test, run

See the [README's "Build from source" section](README.md#build-from-source).
TL;DR:

```pwsh
dotnet build -c Release
dotnet test
dotnet run --project DiffViewer\DiffViewer.csproj
```

Please run `dotnet test` locally before opening a PR. CI
(`.github/workflows/build.yml`) runs the same on every push and PR.

## Per-user state on disk

DiffViewer writes settings and recent-context history under
`%APPDATA%\DiffViewer\`:

- `settings.json` — persisted UI preferences (font, toggles, suppressed
  confirmation flags, etc.).
- `recents.json` — up to 10 most recent launch contexts.

Corrupt or future-version files are renamed to `.bak` rather than
overwritten; see `SettingsService` and `RecentContextsService` for the
exact behavior.

## Notable csproj knobs

`DiffViewer.csproj` has one **load-bearing** publish setting that's easy
to remove by accident:

```xml
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
```

This is what makes LibGit2Sharp's native `git2-*.dll` work inside the
single-file bundle (they get extracted to `%TEMP%` on first launch).
Do **not** remove without testing on a clean machine.

## PR conventions

- Commit messages: free-form, but be specific. No required prefix.
- Branching: work directly on `master` for small, low-risk changes; use a
  feature branch + PR for anything risky or non-trivial.
- Run `dotnet test` before pushing.
- Keep changes surgical and focused. Don't fold unrelated cleanup into a
  feature commit.

## Releases

Release builds are tag-driven and published via
`.github/workflows/release.yml`. See the
[README's "Releases" section](README.md#releases). Don't fold a release
into a normal feature commit — release tags should point at the commit
that should ship.

## License

By contributing, you agree that your contributions will be licensed under
the [MIT License](LICENSE) that covers the project.
