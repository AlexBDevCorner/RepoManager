# Task 35 — Design main table

- Milestone: 7 — Good UX
- Type: UI

Recommended columns:

```text
Repository
Branch
Worktree
Tracking
Vs Default
Update
Last Fetch
```

Example:

```text
┌────────────┬────────────────┬───────┬──────────┬───────────────┬──────────────┬────────────┐
│ Repository │ Branch         │ Files │ Upstream │ vs main       │ Status       │ Last Fetch │
├────────────┼────────────────┼───────┼──────────┼───────────────┼──────────────┼────────────┤
│ Store      │ feature/search │ Clean │ ↑2 ↓0    │ ↑5 ↓3        │ Ahead        │ 2 min ago  │
│ Identity   │ main           │ Clean │ ↑0 ↓4    │ ↑0 ↓4        │ Can update   │ 2 min ago  │
│ FileStore  │ main           │ Dirty │ ↑0 ↓2    │ ↑0 ↓2        │ Dirty        │ 2 min ago  │
│ Search     │ main           │ Clean │ ↑3 ↓2    │ ↑3 ↓2        │ Diverged     │ 2 min ago  │
│ Viewer     │ main           │ Clean │ ↑0 ↓0    │ ↑0 ↓0        │ Up to date   │ 2 min ago  │
└────────────┴────────────────┴───────┴──────────┴───────────────┴──────────────┴────────────┘
```

Do not place every piece of information in the table. Use an expandable/detail section for verbose info (see Task 36).

## Acceptance criteria

- [ ] Table matches columns above with compact formatting.
