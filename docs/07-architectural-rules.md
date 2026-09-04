# 07 — Architectural Rules

Enforce during code review. Worth putting into `AGENTS.md` or developer documentation.

## Rule 1

WPF code must never invoke `git.exe`. Only Infrastructure does that.

## Rule 2

`GitCommandRunner` does not understand Git business concepts. It knows:

```text
working directory
arguments
stdout
stderr
exit code
```

It does not know:

```text
fetch
pull
ahead
behind
safe update
```

## Rule 3

RepositoryInspector is read-only. It may never:

```text
fetch
pull
checkout
merge
rebase
reset
stash
```

## Rule 4

UpdateEligibilityClassifier performs no IO. `Classify(configuration, snapshot)` must be a pure function. That makes safety logic easy to test.

## Rule 5

RepositoryUpdater is the only component allowed to mutate the checked-out branch in v1. Its only permitted mutation is:

```text
git pull --ff-only --no-rebase
```

## Rule 6

Never automatically:

```text
checkout
stash
merge
rebase
reset
```

Even when doing so appears convenient.

## Rule 7

A failed repository operation never aborts an All-Repositories operation. Each repository is independent.

## Rule 8

Never use human-readable Git output when Git provides machine-readable alternatives. Prefer:

```text
--porcelain
--count
--short
symbolic-ref
rev-parse
```

## Rule 9

Every update refusal should have a human-readable reason. The application must be able to answer:

> Why didn't you update this repository?

## Rule 10

Do not overengineer. Specifically avoid introducing:

```text
CQRS
MediatR
event sourcing
message buses
generic repositories
database abstractions
microservices
HTTP APIs
```

unless a future requirement actually needs them. A good architecture here should remain small.
