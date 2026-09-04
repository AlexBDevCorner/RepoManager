# Task 20 — Implement RepositoryRowViewModel

- Milestone: 4 — Read-only MVP
- Type: UI (presentation only)

This object exists solely for presentation.

```csharp
public partial class RepositoryRowViewModel
    : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _branch = string.Empty;

    [ObservableProperty]
    private string _worktreeStatus = string.Empty;

    [ObservableProperty]
    private string _upstreamStatus = string.Empty;

    [ObservableProperty]
    private string _defaultBranchStatus = string.Empty;

    [ObservableProperty]
    private string _updateStatus = string.Empty;
}
```

Mapping: `new Divergence(2, 0)` → `↑2 ↓0`:

```csharp
private static string FormatDivergence(
    Divergence? divergence)
{
    if (divergence is null)
    {
        return "—";
    }

    return $"↑{divergence.Ahead} ↓{divergence.Behind}";
}
```

Do not implement Git business rules here.

## Acceptance criteria

- [ ] Divergence formatting covered (`null` → `—`).
- [ ] No Git logic in ViewModel.
