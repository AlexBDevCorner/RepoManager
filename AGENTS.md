# AGENTS.md — RepoManager

Instructions for coding agents (human-paired assistants, the OpenCode
autonomous worker, reviewers). Normative where marked; everything else is
strong guidance. If this file conflicts with a dispatched task spec, the task
spec wins for *what* to build, this file wins for *how* to build it.

## Project overview

Windows desktop dashboard for managing and monitoring multiple local Git
repositories. Quick overview: branch, clean/dirty tree, ahead/behind upstream
and remote default, last fetch, update eligibility — plus conservative
fast-forward updates.

> **Safety principle:** inspect freely, fetch freely, but mutate the
> checked-out branch only when a safe fast-forward is proven. The final
> safety net is always Git itself: `git pull --ff-only --no-rebase`.

Deliberately **not** a full Git client: no commit/push/checkout UI, no merge,
rebase, reset, stash, conflict resolution, cloning, or history browsing.

## Solution layout

```text
src/RepoDashboard.App/             WPF presentation (net10.0-windows, WinExe)
  Services/ ViewModels/ Views/     Views + dialogs, view-models, DI composition root
src/RepoDashboard.Core/            Domain + application logic (net10.0)
  Dashboard/ Discovery/ Git/       Inspection, discovery, sync orchestration
  Lifetime/ Models/                models, operational state
  Repositories/ State/ Sync/
src/RepoDashboard.Infrastructure/  External systems (net10.0)
  git.exe execution, JSON persistence
tests/
  RepoDashboard.Core.Tests/        unit tests
  RepoDashboard.App.Tests/         view-model tests
  RepoDashboard.IntegrationTests/  real temp git repos, real git.exe behavior
docs/                              design docs; 07-architectural-rules.md is normative
RepoDashboard.slnx                 solution (new .slnx format, needs .NET 10 SDK)
```

Dependency direction: `App -> Core`, `App -> Infrastructure`,
`Infrastructure -> Core`. **Core has no dependency on WPF or Infrastructure.**

## Build / test / run (Windows)

```powershell
dotnet build RepoDashboard.slnx
dotnet test RepoDashboard.slnx
dotnet run --project src/RepoDashboard.App
```

- .NET 10 SDK, no `global.json`. Self-contained publish:
  `dotnet publish src/RepoDashboard.App -c Release -r win-x64 --self-contained true`
- `dotnet test RepoDashboard.slnx` (full suite) is **required verification**
  for every behavior change. Quote commands + results in the PR.

## Architecture rules (normative, see `docs/07-architectural-rules.md`)

1. WPF code must never invoke `git.exe`. Only Infrastructure does that.
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
- All Git access goes through the Infrastructure `git.exe` process wrapper.
  Never LibGit2Sharp, never shell-outs via PowerShell/cmd.
- No secrets, tokens, or machine-specific paths in code or tests.

## Test conventions

- xUnit + FluentAssertions (+ Microsoft.NET.Test.Sdk).
- Integration tests create temporary Git repositories and exercise real Git
  behavior — do not mock Git for the behavior under test.
- Add/update tests with every behavior change. Keep tests deterministic
  (no wall-clock, network, or `%LOCALAPPDATA%` dependencies).
- Config/state persistence tests must isolate storage (temp dirs), never the
  real `%LOCALAPPDATA%\RepoDashboard\`.

## Autonomous worker rules (OpenCode, dispatched via `autonomous-worker.yml`)

- You are given exactly ONE task spec as a read-only `control/` checkout.
  Implement ONLY that task.
- Never edit anything under `control/`. Never invent follow-up tasks, never
  change priorities or dependencies — out-of-scope items belong in new draft
  tasks, not in your PR.
- Work on branch `autonomous/<TASK-ID>` from `master`. Open exactly ONE PR
  to `master`. If the branch/PR already exists (retry), reuse and update it.
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

## Must not modify

- `.github/` — autonomy wiring and CI. Propose changes in the PR body instead.
- `control/` — read-only control-repo checkout (task spec).
- Target frameworks or package versions unless the task requires it (justify
  in the PR if so).
- Anything listed under the task spec's `Out of scope`.
