# Autonomous worker

This workflow depends on the AutonomousWork dispatcher/guard implementation.
Merge that control change before this target workflow.

`autonomous-worker.yml` handles the manual RM-001 pilot and dispatcher claims.
The manual pilot requires the current eligible ready task. Automatic execution
requires an exact persisted attempt UUID at a full control SHA. The worker
rechecks current project/global switches, task content, open PRs, review head,
and execution limits after queueing.

The OpenCode action receives a CI-only permission override. Only runner temp
paths are readable outside the worktree, edits there are denied, shell
redirection of runner command files is denied, and questions/repeated failing
tool loops are denied. The model step times out after 35 minutes and the worker
job after 50 minutes.

Deterministic verification runs in a separate `verify` job on a fresh runner.
It re-checks out the target and the pinned control SHA and verifies actual
GitHub PR state there, so the model cannot modify the verifier's code or the
environment (such as `GITHUB_ENV`/`GITHUB_PATH`) in which its own output is
judged. The `verify` job uses read-only permissions and fails closed when the
control checkout is not clean or does not match the pinned claim.

Corrections come from the deterministic control dispatcher, tied to a trusted
submitted review ID and exact PR head SHA. The previous unbounded `/oc` comment
responder is removed. Comments cannot launch a second worker or bypass counters.

CI restores, builds and runs all test projects on Windows. Require the
`build-and-test` check and a review on master in repository settings; adding the
workflow does not configure branch protection. Merges remain manual.

Required existing credentials: `OPENCODE_API_KEY`, the OpenCode App installation,
and `CONTROL_REPO_TOKEN` with read access to AutonomousWork. No credentials are
stored in this change. See AutonomousWork's `docs/autonomy-operations.md` for
activation, reviewer setup and failure recovery.
