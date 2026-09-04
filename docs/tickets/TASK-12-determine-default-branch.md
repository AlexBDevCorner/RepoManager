# Task 12 — Determine remote default branch

- Milestone: 2 — Repository understanding
- Type: backend

## Goal

Preferred remote defaults to `origin`. Do not hard-code `main` or `master`.

## Resolution algorithm (in order)

### 1. Explicit configuration override

If `DefaultBranchOverride = "develop"` use `origin/develop`.

### 2. Remote HEAD

```text
git symbolic-ref --quiet --short refs/remotes/origin/HEAD
```

Example: `origin/main` → extract `main`.

### 3. Fallback: main

```text
git show-ref --verify --quiet refs/remotes/origin/main
```

If exists: default = `main`.

### 4. Fallback: master

Check `refs/remotes/origin/master`.

### 5. Unknown

If nothing works: `DefaultRemoteBranch = null`. UI shows `Default branch unknown`. Do not guess.

## Acceptance criteria

- [ ] Override respected.
- [ ] `origin/HEAD` resolved when present.
- [ ] `main` → `master` fallback works.
- [ ] Unknown yields null, not a guess.
