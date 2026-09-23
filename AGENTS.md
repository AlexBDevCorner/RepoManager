# AGENTS.md — RepoManager

Instructions for coding agents (human-paired assistants, the OpenCode
autonomous worker, reviewers). Normative where marked; everything else is
strong guidance. If this file conflicts with a dispatched task spec, the task
spec wins for *what* to build, this file wins for *how* to build it.

## Project overview

Cross-platform desktop dashboard for managing and monitoring multiple local Git
repositories. Quick overview: branch, clean/dirty tree, ahead/behind upstream
and remote default, last fetch, update eligibility — plus conservative
fast-forward updates. Runs on Windows and Linux via Avalonia.

> **Safety principle:** inspect freely, fetch freely, but mutate the
> checked-out branch only when a safe fast-forward is proven. The final
> safety net is always Git itself: `git pull --ff-only --no-rebase`.

Deliberately **not** a full Git client: no commit/push/checkout UI, no merge,
rebase, reset, stash, conflict resolution, cloning, or history browsing.

## Solution layout

```text
src/RepoDashboard.App/             Avalonia presentation (net10.0, WinExe)
  Services/ ViewModels/            Windows + dialogs, view-models, DI composition root
src/RepoDashboard.Core/            Domain + application logic (net10.0)
  Dashboard/ Discovery/ Git/       Inspection, discovery, sync orchestration
  Lifetime/ Models/                models, operational state
  Repositories/ State/ Sync/
src/RepoDashboard.Infrastructure/  External systems (net10.0)
  git execution, JSON persistence
tests/
  RepoDashboard.Core.Tests/        unit tests
  RepoDashboard.App.Tests/         view-model tests
  RepoDashboard.IntegrationTests/  real temp git repos, real git behavior
docs/                              design docs; 07-architectural-rules.md is normative
RepoDashboard.slnx                 solution (new .slnx format, needs .NET 10 SDK)
```

Dependency direction: `App -> Core`, `App -> Infrastructure`,
`Infrastructure -> Core`. **Core has no dependency on Avalonia or Infrastructure.**

## Build / test / run (Windows and Linux)

```powershell
dotnet restore RepoDashboard.slnx --locked-mode
dotnet build RepoDashboard.slnx --configuration Release --no-restore
dotnet test RepoDashboard.slnx --configuration Release --no-build --no-restore
dotnet format RepoDashboard.slnx --verify-no-changes --no-restore
dotnet run --project src/RepoDashboard.App
```

- .NET 10 SDK, no `global.json`. Self-contained publish:
  `dotnet publish src/RepoDashboard.App -c Release -r win-x64 --self-contained true`
  Linux: `dotnet publish src/RepoDashboard.App -c Release -r linux-x64 --self-contained true`
- The restore/build/test/format sequence above is **required verification**
  for every behavior change (same gates as CI). Quote commands + results in the PR.
- Build configuration (`Directory.Build.props`, `Directory.Packages.props`,
  `.editorconfig`, package lock files) is the source of truth for
  target framework, analyzers, versions, and formatting; do not duplicate
  those rules here.

## Architecture rules (normative, see `docs/07-architectural-rules.md`)

1. Avalonia code must never invoke `git`. Only Infrastructure does that.
2. `GitCommandRunner` knows only working directory, arguments, stdout,
   stderr, exit code. No Git business concepts (fetch/pull/ahead/behind).
3. `RepositoryInspector` is read-only. Never fetch/pull/checkout/merge/
   rebase/reset/stash.
4. `UpdateEligibilityClassifier.Classify(configuration, snapshot)` is a pure
   function. No IO. That is what makes safety logic easy to test.
5. `RepositoryUpdater` is the ONLY component allowed to mutate the
   checked-out branch, and only via `git pull --ff-only --no-rebase`.
6. Never automatically checkout/stash/merge/rebase/reset, even when
   convenient.
7. A failed single-repository operation never aborts a batch operation.
8. Prefer machine-readable Git output (`--porcelain`, `--count`, `--short`,
   `symbolic-ref`, `rev-parse`) over human-readable output.
9. Every update refusal must carry a human-readable reason.
10. No overengineering: no CQRS/MediatR, event sourcing, message buses,
    generic repositories, database abstractions, microservices, HTTP APIs.

## Coding conventions

- C# latest, `Nullable` + `ImplicitUsings` enabled; match existing style.
- MVVM via CommunityToolkit.Mvvm; DI/hosting via
  Microsoft.Extensions.Hosting + DependencyInjection; logging via `ILogger`
  (no `Console` writes in app code).
- All Git access goes through the Infrastructure `git` process wrapper.
  Never LibGit2Sharp, never shell-outs via PowerShell/cmd.
- No secrets, tokens, or machine-specific paths in code or tests.

## Test conventions

- xUnit + FluentAssertions (+ Microsoft.NET.Test.Sdk).
- Integration tests create temporary Git repositories and exercise real Git
  behavior — do not mock Git for the behavior under test.
- Add/update tests with every behavior change. Keep tests deterministic
  (no wall-clock, network, or local-application-data dependencies).
- Config/state persistence tests must isolate storage (temp dirs), never the
  real local application data directory (`%LOCALAPPDATA%\RepoDashboard\` on
  Windows, `~/.local/share/RepoDashboard/` on Linux).

## Autonomous worker rules (OpenCode, dispatched via `autonomous-worker.yml`)

- You are given exactly ONE task spec as a read-only `control/` checkout.
  Implement ONLY that task.
- Never edit anything under `control/`. Never invent follow-up tasks, never
  change priorities or dependencies — out-of-scope items belong in new draft
  tasks, not in your PR.
- Work on branch `autonomous/<TASK-ID>` from `master`. Push that branch to
  `origin` immediately after creating it. If the branch/PR already exists
  (retry), reuse and update it; never create a second branch or PR for the task.
- Preserve progress remotely throughout long-running work. Commit and push
  after every meaningful milestone, and if a milestone takes longer, make a
  checkpoint commit and push at least every 10–15 minutes. Do not wait until
  the whole task is complete before the first commit or push.
- Checkpoint commits may represent incomplete work. Keep them small enough that
  a human can inspect the implementation history, and use descriptive messages
  such as `checkpoint: migrate main window` or
  `checkpoint: adapt app tests`. Do not squash checkpoint commits merely to
  make history look cleaner.
- For a new implementation with no existing PR, after the first pushed
  checkpoint that creates a diff from `master`, open exactly ONE draft PR to
  `master`. Keep updating and pushing to that same PR while implementation is
  in progress. On retries/corrections, reuse the existing PR.
- A successful autonomous worker run MUST leave the task PR ready for review,
  never draft. After the required verification has run and the task is genuinely
  ready, explicitly mark the PR ready for review before finishing. If the PR is
  still draft, run the equivalent of `gh pr ready <number>`. Do not report
  success while the PR remains draft.
- PR title MUST be `[<TASK-ID>] <concise description>`.
- PR labels MUST include `autonomous`, `autonomous:opencode`, `task:<TASK-ID>`
  (create the `task:<TASK-ID>` label if missing).
- PR body MUST contain: `## Task`, `## Control specification`,
  `## Implementation`, `## Verification`, `## Autonomous execution`.
  `## Task` holds the task ID. `## Control specification` holds the pinned
  spec `<control-repo>@<sha>: <task-path>` (the workflow tells you the exact
  line — copy it verbatim).
  `## Verification` quotes the exact build/test commands and their real
  results — never invent results.
  `## Autonomous execution` states the OpenCode Go model used.
- Never merge the PR, never force-push, never commit secrets.
- GitHub authentication is provided by the workflow. Use ordinary `git push`
  / `gh` operations expected by the worker.
- Never inspect or reconstruct authentication credentials. Never print token
  environment variables. Never run `gh auth token`.
- Authentication failure is a worker failure, not permission to discover
  credentials by another mechanism.

## Must not modify

- `control/` — read-only control-repo checkout (task spec).
- Target frameworks or package versions unless the task requires it (justify
  in the PR if so).
- Anything listed under the task spec's `Out of scope`.
