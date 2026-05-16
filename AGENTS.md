# Repository guide for AI coding assistants

Short orientation for Copilot / Claude / similar agents. Read first before
making changes.

## 0. How to ask the user questions

**Never include a `?` character in prose addressed to the user.** Any
sentence ending in `?` that asks them to decide, choose, confirm,
clarify, or weigh in goes in an `ask_user` tool call — even when the
answer seems obvious. When the set of plausible answers is discrete,
pass them via the tool's `choices` array. This rule applies in every
runtime mode and regardless of whether the runtime reports the user as
available.

## 1. What this repo is

A single WPF desktop application: a side-by-side Git diff viewer for
Windows (with an inline mode toggle). Compares two `DiffSide` values —
`CommitIsh` or `WorkingTree` — so the supported pairings are working
tree vs HEAD, working tree vs a commit, or commit vs commit. The repo
contains two projects at the root:

- `DiffViewer/` — the WPF app (`DiffViewer.csproj`). `net8.0-windows`, WPF,
  `Nullable` enabled, `ImplicitUsings` enabled, `LangVersion` set inline in
  the csproj.
- `DiffViewer.Tests/` — xUnit unit tests covering view-models, services,
  utilities, rendering helpers, and recent-contexts state
  (`DiffViewer.Tests.csproj`). References `DiffViewer.csproj` and reaches
  `internal` types via `InternalsVisibleTo("DiffViewer.Tests")` declared
  in `DiffViewer.csproj`. The test project has no other internal
  dependencies.

Load-bearing third-party libraries (do not propose replacements without
discussion): **CommunityToolkit.Mvvm** (MVVM source generators),
**AvalonEdit** (text / diff rendering), **DiffPlex** (diff algorithm),
**LibGit2Sharp** (in-process Git operations; ships native `git2-*.dll`).

There is no shared library, no `Directory.Build.props` /
`Directory.Packages.props` / `global.json` / `NuGet.config` at the repo
root. Each csproj is fully self-contained: target framework, nullable
settings, and package versions all live in the project file.

## 2. Scope discipline

- Make surgical changes that fully address the request. Do not refactor
  unrelated code, rename files, or reformat untouched areas.
- If you find a tightly-coupled bug caused by the code you're changing,
  fix it. Otherwise, note it and move on.
- Prefer ecosystem tooling (`dotnet new`, `dotnet add package`) over
  manual file or csproj edits.

## 3. When in doubt

- Ask rather than guessing on behavioral choices, defaults, limits, or
  scope (see §0). See §4 for the plan-and-approval flow.
- **Discussion is not approval.** When you ask a clarifying question
  or offer the user a choice among options, their answer is input to
  your plan, not a command to execute. Picking option B from a
  multiple-choice you offered is the user choosing a direction, not
  authorizing the change. Wait for an explicit go-ahead — phrases
  like "implement", "go ahead", "ship it" — before touching code.
- **Prefer the simpler, convention-aligned option over a clever
  alternative.** "Simpler" means less mechanical complexity — fewer
  special cases, less hidden state, less reader cognitive load —
  *not* smaller diff. A clean refactor with more lines changed is
  often simpler than a patch that layers a workaround on a workaround.
  See §4 on not defaulting to minimal change.

## 4. Planning & critical thinking

- **Plan before changing code, every time.** Before writing or
  modifying any code — even a one-line typo fix or "obvious" bug fix
  — propose a short plan: what you will change, in which files, with
  what verification, and at least one viable alternative with
  tradeoffs (or an explicit note that no meaningful alternative
  exists). Surface open questions and wait for the user's explicit
  approval before touching code. Approval covers only the plan as
  presented; any material scope change or newly discovered work
  requires a revised plan and fresh approval.
- **Direct user commands are self-approving (narrow exception).**
  When the user issues an unambiguous, scoped command (e.g., "delete
  file X", "revert commit abc123", "rerun the tests"), the request
  itself is the plan and the approval. Echo back a one-line
  confirmation of exactly what you are about to do, then proceed.
  This bypass applies to the bounded command portion only; any
  adjacent question or implied cleanup still goes through the
  standard plan-and-approve flow. Phrases like "continue",
  "finish it", "take care of the rest", or "do the obvious cleanup"
  are **not** unambiguous commands.
- **Bug reports and feature ideas are plan-triggers, not
  execute-triggers.** A user message that describes a problem ("X is
  broken", "this looks weird", "the spacing is off") or proposes an
  idea ("we should add Y", "what if we...") is a request to
  investigate and propose a plan — it is **not** authorization to
  edit, test, or commit. The execute step requires an unambiguous
  command verb in the same or a later turn ("fix it", "implement
  that", "ship it").
- **CI failures are plan-triggers, not retry-triggers.** Never re-run
  a failed CI job or push a speculative fix on the agent's own
  judgment when CI is red. The default response to any CI failure
  is: tell the user which job failed, summarize what broke (test,
  step, assertion, what the changeset touched), and ask what to do.
  Silent retries hide both flakes and genuine regressions.
- **Use `ask_user`; never "make a best guess on autopilot".** If the
  user is unavailable, ask anyway and stop; do not start
  implementing. An autonomous runtime is not authorization to guess.
- **Treat user suggestions as proposals, not orders.** Evaluate each
  one critically before acting. If you spot a flaw, a missed case,
  a simpler alternative, or a risk the user may not have weighed,
  say so before implementing. Be direct: a concrete recommendation
  with a reason beats vague hedging. Disagree when you have a reason
  to; do not silently comply with a request you believe is wrong or
  risky. After the user decides, follow their decision unless they
  ask you to push back again.
- **Don't default to the minimal change.** Recommending the smaller
  of two competing options because it has less churn is
  risk-aversion masquerading as pragmatism. Judge options on
  long-term architectural fit, not diff size. If your reasoning for
  the smaller option leans on phrases like "less diff," "less
  churn," "less risk of breakage," or "captures most of the
  benefit," those are tactical considerations — stop and re-evaluate
  on architectural grounds. One-time refactor cost is finite and
  reviewable; ongoing cost of structural compromise is not. Present
  the architecturally-correct option as the recommendation; the user
  can still choose the conservative path, but they should choose it
  knowingly, not because the recommendation pre-baked the bias. This
  rule applies to the option-weighing / recommendation phase only;
  once a plan is approved, §2 Scope discipline still governs
  execution.

## 5. Definition of Done

Before declaring a task done:

1. `dotnet build -c Release` succeeds with **no new warnings**.
2. `dotnet test` passes.
3. If you touched anything inside the
   `<PropertyGroup Condition="'$(Configuration)' == 'Release'">` block
   of `DiffViewer.csproj`, `.github/workflows/release.yml`, or the
   publish-related dependency surface (LibGit2Sharp version, native
   library handling), **verify the release publish works**:
   `dotnet publish DiffViewer\DiffViewer.csproj -c Release -o publish`
   must produce a working `publish\DiffViewer.exe`.
   `IncludeNativeLibrariesForSelfExtract=true` is load-bearing for
   LibGit2Sharp's `git2-*.dll`; removing it breaks the single-file
   release silently at runtime.
4. `.editorconfig` is honored; no new diagnostic suppressions beyond
   the existing `CS8618` softening.
5. Package versions for the four load-bearing libraries
   (CommunityToolkit.Mvvm, AvalonEdit, DiffPlex, LibGit2Sharp) were
   not bumped without an explicit reason recorded in the commit body.

## 6. Tech stack (non-negotiable defaults)

- **Runtime:** `net8.0-windows`, WPF, `Nullable enable`,
  `ImplicitUsings enable`, `LangVersion latest`.
- **MVVM:** **CommunityToolkit.Mvvm 8.4.0**. New view-models inherit
  `ObservableObject`, mark the class `partial`, and use
  `[ObservableProperty]` for bindable fields and `[RelayCommand]` for
  commands. Do not hand-roll `INotifyPropertyChanged` in new code.
- **Diff algorithm:** DiffPlex.
- **Editor / syntax rendering:** AvalonEdit.
- **Git access:** LibGit2Sharp (in-process). Do not shell out to
  `git.exe` from production code without explicit discussion — the
  in-process path is what makes the single-file publish self-contained.
- **Tests:** xUnit 2.9 + FluentAssertions 6.12, run via `dotnet test`.

Discuss before introducing new MVVM frameworks, DI containers, UI
toolkits (Avalonia, MAUI, WinUI 3, etc.), logging libraries, or test
frameworks.

Likewise do not bump major versions of the four load-bearing libraries
on a whim — each has known integration quirks (single-file publish for
LibGit2Sharp, theming for AvalonEdit, source-generator behavior for
CommunityToolkit.Mvvm).

## 7. Repository layout

```
DiffViewer/
  ViewModels/        # All view-models live here; reachable from tests
                     # via InternalsVisibleTo
  Models/
  Services/
  Utility/
  Assets/            # diffviewer.ico etc.
  *.xaml / *.xaml.cs # Views and minimal code-behind
DiffViewer.Tests/    # xUnit; mirrors DiffViewer namespace structure
.github/workflows/   # build.yml (CI), release.yml (tag-driven)
```

## 8. Build, test, run

- Build: `dotnet build -c Release`
- Test: `dotnet test`
- Run from source: `dotnet run --project DiffViewer\DiffViewer.csproj`
- Produce a release-equivalent single-file exe (matches the CI release):
  `dotnet publish DiffViewer\DiffViewer.csproj -c Release -o publish`

## 9. Coding conventions

### MVVM discipline

- New view-models inherit `ObservableObject` from CommunityToolkit.Mvvm,
  are declared `partial`, and use `[ObservableProperty]` /
  `[RelayCommand]` source generators. Reference examples:
  `DiffViewer/ViewModels/MainViewModel.cs`,
  `DiffViewer/ViewModels/DiffPaneViewModel.cs`.
- View-models avoid `System.Windows.*` types where reasonable so xUnit
  can host them without spinning up a WPF dispatcher. When a type from
  `System.Windows` is genuinely needed (e.g., a binding-compatible
  enum), isolate it behind an interface that tests can stub.
- Code-behind (`*.xaml.cs`) stays thin — view wiring only, no business
  logic. Logic belongs in a view-model or service.

### WPF threading

- Never block the UI thread on LibGit2Sharp calls or other I/O. Do the
  work on a background thread (`Task.Run` / `async`) and marshal results
  back via `Dispatcher.InvokeAsync` — or rely on `await`'s captured
  `SynchronizationContext` if the caller was on the UI thread.
- View-model property setters raise `PropertyChanged` on the calling
  thread; bound writes must happen on the dispatcher. If a background
  task writes a bindable property directly, that is a bug.

### Nullable / diagnostic suppressions

- `.editorconfig` softens **only** `CS8618` (non-nullable field) to
  `suggestion`, to avoid noise on XAML-bound view-model fields that the
  binder initializes. This is the only allowlisted softening. Do not
  add new `#pragma warning disable` directives or per-file
  `dotnet_diagnostic.*.severity` overrides without discussion.

### Naming

- Files, types, members follow standard C#: `PascalCase` for types and
  members, `camelCase` for locals and parameters, `_camelCase` for
  private fields.
- Use descriptive whole-word names. Prefer `index` over `idx`, `error`
  over `err`, `request` over `req`, `length` over `len`, `previous`
  over `prev`, `current` over `cur`, `temporary` over `tmp`, `value`
  over `val`. Well-known abbreviations (`url`, `id`, `db`, `api`,
  `http`, `json`, `lhs`/`rhs`, `min`/`max`) are fine. Single-letter
  names are acceptable only in:
  - numeric loop counters (`for (int i = 0; ...)`),
  - sort comparators (`list.Sort((a, b) => ...)`),
  - destructured domain components (`var (y, m, d) = ...`),
  - trivial one-liner identity lambdas (`items.Select(p => p.Id)`).

  Anywhere else, including persistent locals and parameters, use a real
  word.

## 10. Testing

- **Tests are required for all logic changes.** The only carve-outs
  are XAML (`*.xaml`) and code-behind (`*.xaml.cs`) doing pure view
  wiring — constructors, event-handler delegation, framework overrides.
  This carve-out only works because §9 forbids logic in those layers;
  if you find yourself wanting it for actual logic, the fix is to move
  the logic into a view-model or service (§9 MVVM discipline), not to
  skip the test. **No test = not done.**
- Tests live in `DiffViewer.Tests/`, mirror the production namespace
  structure (`ViewModels/`, `Services/`, `Utility/`, `Rendering/`,
  `RecentContexts/`, etc.), and run via `dotnet test`.
- Test names describe behavior, not mechanics:
  `LoadCommit_WhenRepoIsEmpty_ShowsEmptyState`, not `TestLoadCommit1`.
- Use FluentAssertions for assertions
  (`result.Should().Be(...)`, `act.Should().Throw<...>()`).
- Internal types are reachable via `[InternalsVisibleTo("DiffViewer.Tests")]`
  in `DiffViewer.csproj`. Do not bump a type's visibility to `public`
  purely to make it testable.

## 11. Git & PR hygiene

- Small, focused commits with imperative subject lines (e.g.,
  `Fix race in MainViewModel watcher suspend`).
- **Stage files explicitly by path.** Never run `git add -A`,
  `git add .`, or `git add --all`. This prevents committing unrelated
  edits, generated files (`bin/`, `obj/`, `publish/`), or session-state
  artifacts.
- **Never run `git rebase` or `git pull --rebase`.** When branches
  diverge, use `git pull --no-rebase` (merge), or stop and ask. The
  rule below ("do not rewrite or force-push") already prohibits the
  destructive form; this rule prohibits the local form too.
- Never commit secrets, `.env`, `bin/`, `obj/`, `publish/`, or editor
  files beyond what `.gitignore` already covers.
- Do not rewrite or force-push shared branches.
- **Never push a release tag on the agent's own judgment.** Tagging
  ships a Windows `.exe` to users via the release workflow and
  requires an unambiguous user command. See §12 "When to release"
  for the cadence rules and the agent-tagging prohibition.

### Session attribution

When the **Copilot CLI runtime** authors a commit or opens a PR, record
the session IDs that produced the work so a future session can resume
the same conversation. Two surfaces, both required when applicable:

**1. Commit trailers (every agent-authored commit).** Add the following
trailers at the end of the commit body. Trailers are the canonical Git
convention for end-of-commit metadata (the same mechanism
`Signed-off-by` uses), are parseable by `git interpret-trailers`, and
are greppable via `git log --grep "AI-Local-Session:"`:

```
AI-Local-Session: <local-session-id>
AI-Cloud-Session: <cloud-session-id>
Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
```

**2. PR description Session block (PRs only, additive to the
trailers).** When the runtime opens a PR — or pushes commits to an
open PR that does not yet have a Session block for the current
session — append a Session block to the end of the PR description so
reviewers see the metadata without clicking into individual commits:

```
---
**Session**
- AI-Local-Session: `<local-session-id>`
- AI-Cloud-Session: `<cloud-session-id>`
```

The key names match the commit-trailer keys so the same grep string
works on both surfaces (`git log --grep "AI-Local-Session:"` and a PR
text search both find the same needle). The Markdown chrome (`---`,
**Session**, bullets, backtick-monospace IDs) is purely for human
readability in the PR UI.

The PR block is **additive**, not a replacement for the commit
trailers; PR descriptions are more visible to reviewers, commit
trailers cover the post-merge `git log` view.

#### Canonical sources

For the Copilot CLI runtime, both IDs come from `workspace.yaml` at
the root of the agent's session folder (typically
`~/.copilot/session-state/<local-session-id>/`):

- `workspace.yaml` -> `id` is the local session ID
  (key on both surfaces: `AI-Local-Session`).
- `workspace.yaml` -> `mc_session_id` is the cloud session ID
  (key on both surfaces: `AI-Cloud-Session`).

Never invent values. If `workspace.yaml` is missing, unreadable, or
lacks either `id` or `mc_session_id`, **stop and surface the problem
to the user** (see §0) before committing or opening a PR. Do not
commit with partial or omitted session attribution: a buried "missing"
note will be skipped past by reviewers, and incomplete attribution
defeats the resume mechanism the trailers exist to support. The user
can fix `workspace.yaml`, manually override the rule for that commit,
or decide to skip — but the agent does not make that call silently.

#### Dedup (PR block only)

**Before appending the PR block**, fetch the current PR description
(e.g., `gh pr view <num> --json body`) and check whether a Session
block for the **current** session's `AI-Local-Session` /
`AI-Cloud-Session` pair is already present. If yes, skip — do not
duplicate. If a Session block from a *different* session is present,
**append** a new block rather than overwriting; the history of which
sessions touched the PR is useful context.

Commit trailers do not need a dedup check — each commit is authored
once, in one session.

This rule applies only to the Copilot CLI runtime. Other agent
runtimes (e.g., Copilot Coding Agent) have different session-ID
semantics and are out of scope until added explicitly.

### Responding to PR review feedback

- Reviewer comments — from humans **and** from bots
  (`copilot-pull-request-reviewer[bot]`, dependabot, code-scanning
  agents, etc.) — are **proposals, not orders**. Evaluate each comment
  against the conventions in this file and prior decisions before
  responding. If a comment conflicts with a deliberate prior decision,
  push back with a reasoned reply — do not silently rewrite the code
  to match.
- **Bot comments carry no special authority.** A bot's confident tone
  is not a reason to action a suggestion that conflicts with the
  conventions here.
- **One reasoned pushback, then escalate.** If you post a pushback
  reply to a bot comment and the bot re-asserts the same concern, do
  not loop into another reply cycle. Escalate to the user with a
  one-paragraph summary of the disagreement and stop.
- **Resolve threads only AFTER pushing the addressing commit, and
  verify the fix landed.** Reviewer bots do not return to verify, so
  leaving addressed threads open is noise that hides genuinely
  unresolved items. For pushback-only responses (no code change),
  resolve only after the user accepts the reply.

## 12. Releases

Tag-driven. Push an annotated tag matching `v[0-9]+.[0-9]+.[0-9]+`
(optionally with a `-suffix` prerelease component, e.g., `v0.2.0-rc1`)
to master. `.github/workflows/release.yml` builds, publishes a
single-file `DiffViewer.exe`, and creates a GitHub Release with the tag
annotation as the body. Don't fold a release into a normal feature
commit — release tags should point at the commit that should ship.

```pwsh
git tag -a v0.2.0 -m "Release notes go here"
git push origin v0.2.0
```

The CI build workflow (`.github/workflows/build.yml`) runs on every
push to `master` and every PR (`dotnet build -c Release` +
`dotnet test`).

### When to release

Each release is a user-visible Windows `.exe` artifact downloaded from
GitHub Releases. There is no auto-update channel and no nightly feed
— releases *are* the distribution. Tag deliberately, not on a fixed
cadence and not per commit.

**Bump rules (pre-1.0 SemVer):**

- User-facing feature added → **minor** bump (e.g., `v0.1.0` →
  `v0.2.0`).
- Bug-fix-only batch → **patch** bump (e.g., `v0.1.0` → `v0.1.1`).
- Pre-1.0 breaking changes ride in minor bumps (standard SemVer
  carve-out for `0.y.z`). Once the project hits `1.0.0`, breaking
  changes require a major bump.

**Skip releases for** doc-only commits, build hygiene, test-only
changes, and pure refactors that don't change shipped behavior. These
sit on `master` until they ride alongside a feature or bug-fix
release.

**Group commits into coherent ships.** Don't let user-facing work
languish unreleased — but don't tag a release for every individual
feature commit either. When a meaningful delta has accumulated,
batch it into one release with notes that describe the user-visible
changes (not the per-commit refactor history).

**Agent rule: a Copilot CLI session must not push a release tag on
its own judgment.** Tagging is a shipping decision and is owned by a
human. The §4 "unambiguous, scoped command" exception applies: if
the user says "cut v0.2.0" or "ship a release", that is the plan and
the approval. A general directive like "tidy things up", "finish the
release work", or "do the obvious cleanup" is **not** authorization
to tag. Recommending a release in conversation is fine and
encouraged when a meaningful delta has accumulated; pushing the tag
without an explicit command is not.

## 13. Origin

DiffViewer was extracted from the [geevensingh/DevTools][devtools]
personal toolkit monorepo in May 2026 using `git filter-repo --path
DiffViewer/ --path DiffViewer.Tests/`. The full commit history before
that extraction lived in DevTools; the same commits exist here with
rewritten hashes (filter-repo always rewrites). External references to
specific DevTools commit SHAs will not resolve in this repo.

[devtools]: https://github.com/geevensingh/DevTools
