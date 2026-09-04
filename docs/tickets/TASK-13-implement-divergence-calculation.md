# Task 13 — Implement divergence calculation

- Milestone: 2 — Repository understanding
- Type: backend

## Interface

```csharp
public interface IDivergenceCalculator
{
    Task<Divergence?> CalculateAsync(
        string repositoryPath,
        string leftRef,
        string rightRef,
        CancellationToken cancellationToken);
}
```

Execute:

```text
git rev-list --left-right --count HEAD...origin/main
```

Example output `4\t7` → `Ahead: 4, Behind: 7` (left = HEAD, right = origin/main).

## Parser

```csharp
private static Divergence ParseDivergence(
    string output)
{
    var parts = output.Split(
        [' ', '\t'],
        StringSplitOptions.RemoveEmptyEntries);

    if (parts.Length != 2)
    {
        throw new InvalidOperationException(
            $"Unexpected rev-list output: {output}");
    }

    return new Divergence(
        Ahead: int.Parse(parts[0]),
        Behind: int.Parse(parts[1]));
}
```

## Usages

- Upstream divergence: `HEAD...origin/feature/search` → `snapshot.UpstreamDivergence`.
- Default branch divergence: `HEAD...origin/main` → `snapshot.DefaultBranchDivergence`.

Keep the two divergences separate (see domain concepts).

## Acceptance criteria (all four tested)

- [ ] `0 ahead, 0 behind`
- [ ] `3 ahead, 0 behind`
- [ ] `0 ahead, 3 behind`
- [ ] `2 ahead, 4 behind`
