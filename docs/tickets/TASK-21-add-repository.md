# Task 21 — Add repository

- Milestone: 4 — Read-only MVP
- Type: UI + backend

Add button `+ Add Repository`. User chooses a directory.

Validate:

1. Directory exists.
2. It is a Git repository.
3. It hasn't already been added.

Then construct:

```csharp
new RepositoryConfiguration
{
    Id = Guid.NewGuid(),
    Name = new DirectoryInfo(path).Name,
    Path = path,
    PreferredRemote = "origin",
    Enabled = true
};
```

Allow the display name to be changed later. Do not silently crawl the whole parent folder yet (see Task 40).

## Acceptance criteria

- [ ] All three validations enforced with clear errors.
- [ ] New repo appears and persists after restart.
