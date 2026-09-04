# Task 38 — Add Last Fetch tracking

- Milestone: 7 — Good UX
- Type: backend

A Git repo doesn't tell you `Repo Dashboard fetched me at 12:41`. Store app metadata in a separate `state.json` (not `repositories.json`):

```csharp
Dictionary<Guid, DateTimeOffset>
```

Conceptually:

```json
{
  "repositories": {
    "abc": {
      "lastSuccessfulFetch": "2026-09-04T12:44:00+03:00"
    }
  }
}
```

Only update the timestamp if fetch succeeded.

## Acceptance criteria

- [ ] Successful fetch updates timestamp; failed fetch doesn't.
- [ ] State survives restart; config file stays clean of operational state.
