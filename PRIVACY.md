# Privacy policy

DiffViewer is a local-only developer tool. It does not collect,
store, or transmit telemetry, usage analytics, crash reports, or
any other background data about its users or their workflows.

## Networked activity

The only networked activity DiffViewer performs is on the user's
direct action: fetching a GitHub pull request the user has
explicitly asked it to review, by calling the
[GitHub REST API](https://docs.github.com/en/rest) on the user's
behalf.

These calls are made only when:

- The user launches DiffViewer with a GitHub pull request URL on the
  command line, or
- The user picks a previously-launched pull request from the
  recent-contexts dropdown.

No other networked activity is performed. DiffViewer does not phone
home, does not check for updates against any non-GitHub server, and
does not contact any third-party service.

## GitHub credentials

To call the GitHub REST API, DiffViewer needs a GitHub OAuth token.
It obtains one on-demand at the moment of an outbound API call by
shelling out to the [GitHub CLI](https://cli.github.com/)'s
`gh auth token` command, using the token the user already configured
via `gh auth login`. The token is held in memory only for the
duration of that single API call and is never persisted to disk by
DiffViewer.

DiffViewer does not implement its own GitHub OAuth flow, does not
ask the user to paste a personal access token into its settings, and
does not store GitHub credentials anywhere on disk.

## Local state on disk

DiffViewer writes a small amount of user-preference state under
`%APPDATA%\DiffViewer\`:

- `settings.json` — UI preferences (font, side-by-side / inline
  toggle, ignore-whitespace toggle, suppressed-confirmation flags,
  repo-roots list, etc.).
- `recents.json` — up to 10 most recent launch contexts (which
  repo, which commit pair or pull request) so the user can re-open
  them with one click.

This state is local to the user's machine, is never transmitted, and
can be deleted at any time by removing the `%APPDATA%\DiffViewer\`
folder.

## Repository data

DiffViewer reads from the user's local Git repositories using
[LibGit2Sharp](https://github.com/libgit2/libgit2sharp) and, for
hunk write operations (stage / unstage / revert), shells out to
`git.exe`. All of this is local-disk activity — no repository
contents are ever transmitted off the user's machine.

For pull request review, DiffViewer fetches the pull request's head
and base commits into the user's local clone under its own
`refs/diffviewer/...` namespace. These fetches go to GitHub's
servers via the user's existing `origin` / configured remote, using
the user's existing Git credentials. DiffViewer does not push or
write to remote repositories.

## Changes to this policy

Material changes to this policy will be called out in
[CHANGELOG.md](CHANGELOG.md) and in the corresponding GitHub
Release notes.
