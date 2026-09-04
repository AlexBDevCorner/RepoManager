# Task 37 — Add convenience actions

- Milestone: 7 — Good UX
- Type: UI

Per repository:

```text
Refresh
Fetch
Update
Open Folder
Open Terminal
Copy Path
Remove
```

Open folder:

```csharp
Process.Start(new ProcessStartInfo
{
    FileName = repository.Path,
    UseShellExecute = true
});
```

Open terminal first implementation: `wt.exe -d C:\Source\Repos\Store`. If Windows Terminal isn't available, action may be disabled initially. Editor integration is not a v1 requirement.

## Acceptance criteria

- [ ] All actions work per row; terminal gracefully disabled if `wt.exe` missing.
