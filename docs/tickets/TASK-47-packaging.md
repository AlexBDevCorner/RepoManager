# Task 47 — Add packaging

- Milestone: 8 — Convenience and hardening
- Type: release

Only after the app works. Create Windows release build:

```powershell
dotnet publish src/RepoDashboard.App -c Release -r win-x64 --self-contained true
```

Consider `win-x64` / self-contained / single-file. Investigate single-file afterward. Don't let installer creation block MVP — first target is `RepoDashboard.exe` in a release directory.

## Acceptance criteria

- [ ] `dotnet publish` release produces runnable `RepoDashboard.exe`.
