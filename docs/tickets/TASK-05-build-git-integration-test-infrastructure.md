# Task 5 — Build Git integration test infrastructure

- Milestone: 1 — Git foundation
- Type: testing
- Note: do this before sophisticated inspection logic — saves debugging later.

## Goal

Create repositories dynamically inside the test directory. Do not depend on the developer's real repositories.

Test layout:

```text
TemporaryDirectory/

    remote.git/

    repo-a/

    repo-b/
```

`remote.git` is a bare Git repository. Both repo-a and repo-b clone it.

## Git test helper

Create `GitTestRepositoryFactory` with methods:

```csharp
CreateBareRepositoryAsync()
CloneAsync()
CommitFileAsync()
PushAsync()
CheckoutAsync()
CreateBranchAsync()
```

Configure identity locally:

```text
git config user.email test@example.com
git config user.name RepoDashboardTests
```

Never rely on the developer's global Git configuration.

## Example scenario

Initial `remote/main = A`; repo-a = `A`; repo-b = `A`. repo-a creates B and pushes → `remote/main = A-B`. repo-b remains `A` → now `Ahead: 0, Behind: 1`. This must be generatable automatically during a test.

## Scenarios the helper must eventually create

```text
Up to date
Behind
Ahead
Diverged
Dirty
Detached HEAD
No upstream
Feature branch
Different default branch
```

## Acceptance criteria

- [ ] One integration test can create two clones, commit from one, push, fetch from the other and verify Git reports the second is behind.
