# Task 15 — Define update eligibility

- Milestone: 3 — Safety engine
- Type: backend (design, no Git execution yet)

## Goal

Make the decision logic completely independent of Git execution.

```csharp
public enum UpdateEligibility
{
    CanFastForward,

    AlreadyUpToDate,

    Ahead,

    Diverged,

    Dirty,

    NoUpstream,

    DetachedHead,

    OperationInProgress,

    RepositoryMissing,

    InvalidRepository,

    UpstreamUsesDifferentRemote,

    Unknown
}
```

```csharp
public sealed record UpdateDecision(
    UpdateEligibility Eligibility,
    string Explanation)
{
    public bool CanUpdate =>
        Eligibility == UpdateEligibility.CanFastForward;
}
```

```csharp
public interface IUpdateEligibilityClassifier
{
    UpdateDecision Classify(
        RepositoryConfiguration configuration,
        RepositorySnapshot snapshot);
}
```

## Acceptance criteria

- [ ] Enum + record + interface exist in Core with no IO dependencies.
