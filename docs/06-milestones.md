# 06 — Milestones

## Milestone 1 — Git foundation (Tasks 1–5)

Outcome:

```text
Application runs
Git commands can be executed safely
Test repositories can be generated
```

## Milestone 2 — Repository understanding (Tasks 6–14)

Outcome: given any configured repository, the application understands:

```text
branch
dirty state
upstream
default branch
ahead/behind
Git operation state
```

This is the most important technical milestone.

## Milestone 3 — Safety engine (Tasks 15–17)

Outcome: the application can answer:

```text
Can I safely update this repository?
```

without actually changing anything.

## Milestone 4 — Read-only MVP (Tasks 18–23)

Outcome: useful desktop application. You can:

```text
add repositories
see branches
see dirty state
see divergence
refresh
remove repositories
open folders
```

Recommendation: start using the application at this point. Real usage will expose incorrect assumptions before mutation features are introduced.

## Milestone 5 — Fetch (Tasks 24–27)

Outcome:

```text
Fetch
Fetch All
```

work safely. Remote state becomes genuinely useful.

## Milestone 6 — Safe updates (Tasks 28–31)

Outcome: the app can update repositories automatically while refusing anything potentially dangerous.

## Milestone 7 — Good UX (Tasks 32–39)

Outcome: the application feels like an actual tool rather than a technical demo.

## Milestone 8 — Convenience and hardening (Tasks 40–47)

Outcome:

```text
repository discovery
logging
cancellation
error handling
complete tests
release build
```

> Implementation advice: finish the read-only MVP before implementing pull. Use it against real repositories for a few days and validate that branch/upstream/default-branch detection behaves exactly as expected. Once that information is trustworthy, adding the safe-update operation is quite small; doing it in the reverse order would make Git mutation the thing debugging your inspection logic.
