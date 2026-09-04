# Task 4 — Verify Git installation

- Milestone: 1 — Git foundation
- Type: backend / UX

## Goal

Provide a clear startup error if Git cannot be found.

Execute:

```text
git --version
```

This command doesn't require a repository.

Extend the runner or create `IGitEnvironment`:

```csharp
public interface IGitEnvironment
{
    Task<GitEnvironmentInfo> CheckAsync(
        CancellationToken cancellationToken);
}
```

Return:

```csharp
public sealed record GitEnvironmentInfo(
    bool Available,
    string? Version,
    string? Error);
```

Display either:

```text
Git 2.51.0 detected
```

or:

```text
Git could not be found.

Install Git for Windows and ensure git.exe
is available through PATH.
```

Do not let every repository subsequently fail with obscure process errors.

## Acceptance criteria

- [ ] App shows version when Git is available.
- [ ] Removing Git from PATH produces one clear application-level error.
