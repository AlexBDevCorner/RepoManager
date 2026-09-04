# Task 6 — Implement repository configuration persistence

- Milestone: 2 — Repository understanding
- Type: backend

## Goal

Store which repositories the user wants to monitor. Do not store transient Git state here.

## Interface

```csharp
public interface IRepositoryConfigurationStore
{
    Task<IReadOnlyList<RepositoryConfiguration>>
        LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(
        IReadOnlyCollection<RepositoryConfiguration> repositories,
        CancellationToken cancellationToken);
}
```

## Location

Store under:

```text
%LOCALAPPDATA%\RepoDashboard\repositories.json
```

Resolve via `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)`. Never assume `C:\Users\Somebody`.

## JSON format

```json
{
  "repositories": [
    {
      "id": "...",
      "name": "Store",
      "path": "C:\\Source\\Repos\\Store",
      "preferredRemote": "origin",
      "defaultBranchOverride": null,
      "enabled": true
    }
  ]
}
```

## Atomic save

Write `repositories.json.tmp` first, then replace `repositories.json`:

```csharp
var temporaryPath = path + ".tmp";

await File.WriteAllTextAsync(
    temporaryPath,
    json,
    cancellationToken);

File.Move(
    temporaryPath,
    path,
    overwrite: true);
```

## Path normalisation

Before comparing: `Path.GetFullPath(path)`, trim trailing separators, compare with `StringComparer.OrdinalIgnoreCase` (Windows app). Reject duplicates like `C:\Source\Repos\Store` vs `c:\source\repos\store\`.

## Acceptance criteria

- [ ] Repository can be added, app restarted, repository still listed.
- [ ] Duplicate paths are rejected (case-insensitive, normalized).
- [ ] Crash cannot leave half-written JSON.
