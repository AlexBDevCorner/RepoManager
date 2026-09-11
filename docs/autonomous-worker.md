# Autonomous worker

Pilot implementation of AutonomousWork steps 8–10 for RepoManager. The worker
is manually triggered (no dispatcher yet — step 11). One dispatch implements
exactly one control-repo task and produces exactly one PR.

## Dispatching

Actions tab → `autonomous-worker` → Run workflow (or via CLI):

```powershell
gh workflow run autonomous-worker.yml `
  -f task_id=RM-001 `
  -f task_path=projects/repomanager/tasks/RM-001.md
```

Inputs:

| Input | Required | Default | Meaning |
| --- | :---: | --- | --- |
| `task_id` | ✅ | — | Control-repo task ID, e.g. `RM-001` |
| `task_path` | ✅ | — | Spec path inside the control repo, e.g. `projects/repomanager/tasks/RM-001.md` |
| `control_repo` | ✅ | `AlexBDevCorner/AutonomousWork` | Control repository (`Owner/Repo`) |
| `control_commit` | ❌ | `""` (default branch HEAD) | Pin a control commit for reproducibility |
| `model` | ❌ | `opencode-go/kimi-k3` | OpenCode model (`provider/model`) |

Prerequisites: the `opencode-agent` GitHub App installed on this repository,
the `OPENCODE_API_KEY` (OpenCode Go subscription) repository secret, and a
`CONTROL_REPO_TOKEN` secret. The control repository is private and a
workflow's `github.token` cannot read other repositories, so control checkout
authenticates with `CONTROL_REPO_TOKEN`: a fine-grained PAT with Contents:
Read on the control repository only.

What a run does:

1. `guard` — refuses to start while a *different* autonomous task has an open
   PR (single-active-task rule). A PR counts as autonomous when it carries
   the `autonomous` label **or** its head branch starts with `autonomous/`.
   Re-dispatching the *same* task (`autonomous/<TASK-ID>` still open) is
   allowed as the retry path.
2. `worker` — checks out this repo plus the control repo (read-only
   `control/`), deterministically validates the task identity (spec file
   exists, file name and front-matter `id` equal `task_id`, the spec's
   project maps to this repository), then requires control-repo
   eligibility: `autonomous-work next <project>` at the pinned checkout
   must select exactly the dispatched task (human-authorized `ready`,
   dependencies done, project enabled, capacity free — dispatching any
   other task fails before OpenCode starts). It records the exact control
   SHA, sets up .NET 8 + 10 (control CLI + target), ensures labels, runs
   OpenCode with the stable wrapper prompt, then strictly verifies PR
   metadata.
3. The workflow fails unless exactly one PR has ever existed on
   `autonomous/<TASK-ID>` targeting `master` — a run with no PR, several
   PRs, a redispatch after merge, or missing evidence sections is a failed
   run. A retry reuses the same PR (reopened automatically when closed).

## Autonomous PR metadata (step 9)

Every worker-created PR is identifiable and machine-linkable
(`Task ↔ PR ↔ Repository`):

- Branch: `autonomous/<TASK-ID>` (e.g. `autonomous/RM-001`). At most one PR
  may ever exist per task branch — retries reuse (and reopen) it, so the
  `Task ↔ PR ↔ Repository` mapping stays permanent.
- Title: `[<TASK-ID>] <concise description>` (e.g. `[RM-001] Add …`).
- Labels: `autonomous`, `autonomous:opencode`, `task:<TASK-ID>`.
  The workflow creates missing labels; the agent applies them.
- Body sections (all required):
  `## Task`, `## Control specification`, `## Implementation`,
  `## Verification`, `## Autonomous execution`.
  `## Control specification` records the pinned spec,
  `<control-repo>@<sha>: <task-path>`, so every PR is traceable to the exact
  control commit it implemented. `## Verification` quotes exact build/test
  commands + real results (invented results fail review);
  `## Autonomous execution` states OpenCode Go + model.

The prompt instructs the agent to follow this contract; the final workflow
step repairs the title prefix and labels automatically but never invents
body evidence — a PR missing any required section, the pinned SHA, or the
`master` base fails the run.

## Single active task (step 10)

Two protections, not one:

1. **Workflow concurrency** (`autonomous-worker.yml`):
   `group: autonomous-worker`, `cancel-in-progress: false`. Runs queue
   instead of overlapping. The group is per repository, so RepoManager and
   MandarinBotNet still make progress concurrently.
2. **Open-PR gate**: the `guard` job fails fast when an open `autonomous` PR
   for a *different* task exists. The future dispatcher (step 11) must apply
   the same rule before triggering: *don't schedule while an open
   `autonomous` PR exists for that project*.

Effect: RepoManager can never implement two autonomous tasks simultaneously.

## Review-fix loop (preview of step 15)

Reviewers (human or ChatGPT) request changes on the PR; a trusted
actor posts `/oc …` with the findings and the `opencode` workflow updates the
same PR (same branch, no second PR). Only `OWNER`/`MEMBER`/`COLLABORATOR`
comments trigger it. Full round caps arrive with step 16.
