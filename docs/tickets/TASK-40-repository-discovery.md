# Task 40 — Add repository discovery

- Milestone: 8 — Convenience and hardening
- Type: feature

Button `Discover Repositories...`. User selects e.g. `C:\Source\Repos`. Search recursively. Do not auto-add — show checklist:

```text
Select repositories to add

☑ Store
☑ Viewer
☑ FileStore
☐ OldPrototype
☐ TempExperiment
☑ Identity
```

Then `Add Selected`.

## Discovery implementation

Do not scan enormous structures without limits. Initial rules:

```text
Maximum depth: 3
Skip hidden folders where useful
Stop descending once a Git repository is found
```

If `C:\Source\Repos\Store` is a repo, don't scan inside Store.

## Acceptance criteria

- [ ] Depth-limited scan, stops at repo roots, explicit user confirmation before add.
