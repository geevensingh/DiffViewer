# Repository guide for AI coding assistants

Short orientation for Copilot / Claude / similar agents. Read first before
making changes.

## What this repo is

A single WPF desktop application: a 3-way left/right Git diff viewer. The
repo contains two projects at the root:

- `DiffViewer/` — the WPF app (`DiffViewer.csproj`). `net8.0-windows`, WPF,
  `Nullable` enabled, `ImplicitUsings` enabled, `LangVersion` set inline in
  the csproj.
- `DiffViewer.Tests/` — xUnit unit tests for the view-models
  (`DiffViewer.Tests.csproj`). References `DiffViewer.csproj`. The test
  project has no other internal dependencies.

There is no shared library, no `Directory.Build.props` / `Directory.Packages.props` /
`global.json` / `NuGet.config` at the repo root. Each csproj is fully
self-contained: target framework, nullable settings, and package versions
all live in the project file.

## Build, test, run

- Build: `dotnet build -c Release`
- Test: `dotnet test`
- Run from source: `dotnet run --project DiffViewer\DiffViewer.csproj`
- Produce a release-equivalent single-file exe (matches the CI release):
  `dotnet publish DiffViewer\DiffViewer.csproj -c Release -o publish`

## Releases

Tag-driven. Push an annotated `vX.Y.Z` tag to master and
`.github/workflows/release.yml` builds + publishes. Don't fold a release
into a normal feature commit — release tags should point at the commit that
should ship.

```pwsh
git tag -a v0.2.0 -m "Release notes go here"
git push origin v0.2.0
```

## Conventions

- Code style: enforced by `.editorconfig`. The only deliberate softening is
  CS8618 (non-nullable field) demoted to `suggestion` to avoid noise on
  XAML-bound view-model fields.
- Commit messages: free-form. Be specific. No required prefix.
- Branching: work on `master` for small changes; use a feature branch + PR
  for anything risky. CI (`.github/workflows/build.yml`) runs on every push
  to `master` and every PR.

## Origin

DiffViewer was extracted from the [geevensingh/DevTools][devtools] personal
toolkit monorepo in May 2026 using `git filter-repo --path DiffViewer/
--path DiffViewer.Tests/`. The full commit history before that extraction
lived in DevTools; the same commits exist here with rewritten hashes
(filter-repo always rewrites). External references to specific DevTools
commit SHAs will not resolve in this repo.

[devtools]: https://github.com/geevensingh/DevTools
