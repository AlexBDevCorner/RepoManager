# Task 17 — Unit-test every update state

- Milestone: 3 — Safety engine
- Type: testing (safety-critical, target ~100% branch coverage)

## Example

```csharp
[Fact]
public void Dirty_repository_cannot_be_updated()
{
    var snapshot = CreateSnapshot(
        dirty: true,
        upstream: "origin/main",
        ahead: 0,
        behind: 5);

    var result =
        _sut.Classify(Configuration, snapshot);

    result.Eligibility
        .Should()
        .Be(UpdateEligibility.Dirty);
}
```

## Required cases

```text
Missing
Invalid
Detached
Merge in progress
Rebase in progress
Cherry-pick in progress
Dirty
No upstream
Different remote
Up-to-date
Ahead
Behind (CanFastForward)
Diverged
Unknown divergence
```

## Acceptance criteria

- [ ] One test per state above, all passing.
