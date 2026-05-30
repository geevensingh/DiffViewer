# Code signing policy

DiffViewer aims to ship its Windows release binaries signed with a
free open-source code signing certificate provided by the
[SignPath Foundation](https://signpath.org/) via
[SignPath.io](https://about.signpath.io/).

> **Status:** Application to the SignPath Foundation is pending. Until
> the certificate is in place, release binaries are unsigned and
> Windows SmartScreen warns on first run; see the README's "Install"
> section for the click-through path.

## Project scope

This policy applies to all release artifacts published to the
[GitHub Releases page](https://github.com/geevensingh/DiffViewer/releases).
Today that is a single-file portable `DiffViewer.exe`; once auto-update
support ships, a Setup installer will be added alongside it and will
also be covered by this policy.

## Team roles

DiffViewer is a single-maintainer open-source project.

- **Committers and reviewers:** [@geevensingh](https://github.com/geevensingh)
- **Approvers:** [@geevensingh](https://github.com/geevensingh)

## Privacy policy

DiffViewer is a local-only developer tool. The only networked activity
it performs is on direct user action: fetching a GitHub pull request
the user has explicitly asked it to review, by calling the GitHub
REST API on the user's behalf with credentials they obtained via
`gh auth login`. No telemetry, usage tracking, analytics, or
background networked activity is performed.

User-supplied GitHub OAuth tokens are obtained on-demand from
`gh auth token` at the moment of an outbound API call, held in memory
only for the duration of that call, and are never persisted to disk
by DiffViewer.
