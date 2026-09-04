# Task 41 — Add structured logging

- Milestone: 8 — Convenience and hardening
- Type: backend

Use `Microsoft.Extensions.Logging`. Log repository, operation, started/completed, duration, exit code, error. Example:

```text
Information:
Fetching repository Store

Information:
Fetch completed for Store in 1.34 sec

Warning:
Fetch failed for Identity. Git exit code 128.
```

Do not log secrets, env vars with credentials, or attempt to extract passwords/tokens from Git config.

## Acceptance criteria

- [ ] All fetch/update ops logged with duration + exit code; no secrets logged.
