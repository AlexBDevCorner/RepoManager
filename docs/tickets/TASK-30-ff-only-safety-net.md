# Task 30 — Keep Git's `--ff-only` as final safety net

- Milestone: 6 — Safe updates
- Type: backend (safety)

Even after classification, state can change (external process modifies Git between classify and pull). Never assume classification is enough. Always use `--ff-only`. Git itself is the final authority.

If Git refuses: `Update failed safely`. Do not try another strategy. Especially do not fall back to `merge` / `rebase` / `reset` / `stash`.

## Acceptance criteria

- [ ] Every auto-pull uses `--ff-only`.
- [ ] Refused pull surfaces as safe failure, no fallback mutation.
