# Task 36 — Repository details panel

- Milestone: 7 — Good UX
- Type: UI

When a repository is selected, show:

```text
Store

Path
C:\Source\Repos\Store

Current branch
feature/search

Upstream
origin/feature/search

Remote default
origin/main

Current branch vs upstream
2 ahead / 0 behind

Current branch vs origin/main
5 ahead / 3 behind

Working tree
Clean

Last successful fetch
12:33:51

Last operation
Fetch successful

Remote
origin
```

On failure also show:

```text
Git error

fatal: unable to access ...
```

Keeps the table compact while exposing diagnostics.

## Acceptance criteria

- [ ] All fields above visible for selection; raw Git error on failure.
