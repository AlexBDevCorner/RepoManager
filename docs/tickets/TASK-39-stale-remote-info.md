# Task 39 — Show stale remote information

- Milestone: 7 — Good UX
- Type: UI

Until fetch occurs, remote refs may be old (e.g. `origin/main` = yesterday's state). Communicate subtly:

```text
Last fetch: 3 days ago
```

or:

```text
Remote state may be stale
```

Do not pretend local remote-tracking refs represent the current server.

## Acceptance criteria

- [ ] Stale indicator shown based on Last Fetch age.
