# Task 25 — Refresh status after fetch

- Milestone: 5 — Fetch
- Type: backend

After `git fetch` always run `InspectAsync(...)` again. Before fetch `origin/main = A`; after fetch `origin/main = A-B-C-D` — previously calculated divergence is obsolete.

Do not mutate individual fields manually. Bad: `snapshot.DefaultBranchDivergence = new Divergence(...)`. Good: `snapshot = await _inspector.InspectAsync(...)`. Re-inspect from Git.

## Acceptance criteria

- [ ] Every successful fetch triggers a full re-inspection.
- [ ] No manual snapshot patching.
