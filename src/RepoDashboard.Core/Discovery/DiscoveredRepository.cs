namespace RepoDashboard.Core.Discovery;

/// <summary>
/// One Git repository root found by discovery (Task 40).
/// Pure filesystem result — whether it is already tracked is resolved
/// by the caller against configuration, not here.
/// </summary>
public sealed record DiscoveredRepository
{
    public required string Path { get; init; }

    public required string Name { get; init; }
}
