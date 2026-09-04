namespace RepoDashboard.Core.Models;

/// <summary>
/// Something the user explicitly added to monitor.
/// Persisted as configuration; transient Git state is never stored here.
/// </summary>
public sealed record RepositoryConfiguration
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Path { get; init; }

    public string PreferredRemote { get; init; } = "origin";

    public string? DefaultBranchOverride { get; init; }

    public bool Enabled { get; init; } = true;
}
