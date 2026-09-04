# Task 32 — Add operation state to UI

- Milestone: 7 — Good UX
- Type: UI

Each repository needs presentation state independent of Git state:

```csharp
public enum RepositoryActivity
{
    Idle,
    Refreshing,
    Fetching,
    Updating,
    Completed,
    Failed,
    Skipped
}
```

Examples:

```text
Store — Fetching...
Identity — Updating...
Search — Skipped — working tree dirty
FileStore — Updated
Viewer — Fetch failed
```

Better than opening modal dialogs repeatedly.

## Acceptance criteria

- [ ] Per-repo activity visible inline, no modal spam.
