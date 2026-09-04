# Repo Dashboard — Docs Index

Windows desktop application that monitors a manually selected set of local Git repositories and makes it easy to understand their current state.

Key principle:

> The application may inspect freely, fetch freely, but mutate the checked-out branch only when it can prove that the operation is a safe fast-forward.

## Tech requirements & design

- [01 — Goal and Scope](01-goal-and-scope.md)
- [02 — Tech Requirements](02-tech-requirements.md)
- [03 — Solution Architecture](03-solution-architecture.md)
- [04 — High-Level Architecture](04-high-level-architecture.md)
- [05 — Domain Concepts](05-domain-concepts.md)
- [06 — Milestones](06-milestones.md)
- [07 — Architectural Rules](07-architectural-rules.md)
- [08 — Final Structure and First Release](08-final-structure-and-release.md)

## Backlog tickets (Tasks 1–47)

### Milestone 1 — Git foundation (Tasks 1–5)

- [Task 1 — Create the solution](tickets/TASK-01-create-solution.md)
- [Task 2 — Configure dependency injection](tickets/TASK-02-configure-dependency-injection.md)
- [Task 3 — Implement GitCommandRunner](tickets/TASK-03-implement-git-command-runner.md)
- [Task 4 — Verify Git installation](tickets/TASK-04-verify-git-installation.md)
- [Task 5 — Build Git integration test infrastructure](tickets/TASK-05-build-git-integration-test-infrastructure.md)

### Milestone 2 — Repository understanding (Tasks 6–14)

- [Task 6 — Implement repository configuration persistence](tickets/TASK-06-repository-configuration-persistence.md)
- [Task 7 — Detect whether a folder is a Git repository](tickets/TASK-07-detect-git-repository.md)
- [Task 8 — Read current branch and detached HEAD](tickets/TASK-08-read-branch-and-detached-head.md)
- [Task 9 — Read working-tree status](tickets/TASK-09-read-working-tree-status.md)
- [Task 10 — Detect merge/rebase/cherry-pick state](tickets/TASK-10-detect-operation-state.md)
- [Task 11 — Determine current branch upstream](tickets/TASK-11-determine-upstream.md)
- [Task 12 — Determine remote default branch](tickets/TASK-12-determine-default-branch.md)
- [Task 13 — Implement divergence calculation](tickets/TASK-13-implement-divergence-calculation.md)
- [Task 14 — Complete RepositoryInspector](tickets/TASK-14-complete-repository-inspector.md)

### Milestone 3 — Safety engine (Tasks 15–17)

- [Task 15 — Define update eligibility](tickets/TASK-15-define-update-eligibility.md)
- [Task 16 — Implement update classifier](tickets/TASK-16-implement-update-classifier.md)
- [Task 17 — Unit-test every update state](tickets/TASK-17-unit-test-update-states.md)

### Milestone 4 — Read-only MVP (Tasks 18–23)

- [Task 18 — Implement application-level repository service](tickets/TASK-18-implement-dashboard-service.md)
- [Task 19 — Build the first useful WPF screen](tickets/TASK-19-build-first-wpf-screen.md)
- [Task 20 — Implement RepositoryRowViewModel](tickets/TASK-20-implement-row-viewmodel.md)
- [Task 21 — Add repository](tickets/TASK-21-add-repository.md)
- [Task 22 — Remove repository](tickets/TASK-22-remove-repository.md)
- [Task 23 — Implement local Refresh](tickets/TASK-23-implement-local-refresh.md)

### Milestone 5 — Fetch (Tasks 24–27)

- [Task 24 — Implement RepositoryFetcher](tickets/TASK-24-implement-repository-fetcher.md)
- [Task 25 — Refresh status after fetch](tickets/TASK-25-refresh-after-fetch.md)
- [Task 26 — Implement bounded Fetch All](tickets/TASK-26-bounded-fetch-all.md)
- [Task 27 — Prevent simultaneous operations on one repository](tickets/TASK-27-per-repository-locks.md)

### Milestone 6 — Safe updates (Tasks 28–31)

- [Task 28 — Implement RepositoryUpdater](tickets/TASK-28-implement-repository-updater.md)
- [Task 29 — Implement safe update algorithm](tickets/TASK-29-safe-update-algorithm.md)
- [Task 30 — Keep Git's `--ff-only` as final safety net](tickets/TASK-30-ff-only-safety-net.md)
- [Task 31 — Implement Update Safe Repositories](tickets/TASK-31-update-safe-repositories.md)

### Milestone 7 — Good UX (Tasks 32–39)

- [Task 32 — Add operation state to UI](tickets/TASK-32-operation-state-ui.md)
- [Task 33 — Add aggregate operation summary](tickets/TASK-33-aggregate-summary.md)
- [Task 34 — Implement useful status explanations](tickets/TASK-34-status-explanations.md)
- [Task 35 — Design main table](tickets/TASK-35-design-main-table.md)
- [Task 36 — Repository details panel](tickets/TASK-36-repository-details-panel.md)
- [Task 37 — Add convenience actions](tickets/TASK-37-convenience-actions.md)
- [Task 38 — Add Last Fetch tracking](tickets/TASK-38-last-fetch-tracking.md)
- [Task 39 — Show stale remote information](tickets/TASK-39-stale-remote-info.md)

### Milestone 8 — Convenience and hardening (Tasks 40–47)

- [Task 40 — Add repository discovery](tickets/TASK-40-repository-discovery.md)
- [Task 41 — Add structured logging](tickets/TASK-41-structured-logging.md)
- [Task 42 — Improve Git failure classification](tickets/TASK-42-git-failure-classification.md)
- [Task 43 — Add cancellation](tickets/TASK-43-cancellation.md)
- [Task 44 — Handle application shutdown](tickets/TASK-44-application-shutdown.md)
- [Task 45 — Add complete integration test matrix](tickets/TASK-45-integration-test-matrix.md)
- [Task 46 — Add UI tests only where valuable](tickets/TASK-46-ui-tests.md)
- [Task 47 — Add packaging](tickets/TASK-47-packaging.md)
