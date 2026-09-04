namespace RepoDashboard.Core.Models;

/// <summary>
/// The Git state of one repository at one point in time.
/// Produced by <c>IRepositoryInspector</c>; never persisted as configuration.
/// </summary>
public sealed record RepositorySnapshot
{
    public required Guid RepositoryId { get; init; }

    public required string Path { get; init; }

    public bool DirectoryExists { get; init; }

    public bool IsGitRepository { get; init; }

    public string? CurrentBranch { get; init; }

    public bool IsDetachedHead { get; init; }

    /// <summary>
    /// Short HEAD commit hash when detached, for display
    /// (for example <c>Detached HEAD @ a84c019</c>).
    /// </summary>
    public string? DetachedHeadSha { get; init; }

    public bool IsDirty { get; init; }

    public string? UpstreamRef { get; init; }

    public string? UpstreamRemote { get; init; }

    public string? UpstreamBranch { get; init; }

    /// <summary>
    /// Bare branch name of the preferred remote's default branch
    /// (for example <c>main</c>, not <c>origin/main</c>).
    /// Null when it cannot be determined — never a guess.
    /// </summary>
    public string? DefaultRemoteBranch { get; init; }

    /// <summary>
    /// Current branch vs its upstream
    /// (for example <c>feature/search</c> vs <c>origin/feature/search</c>).
    /// </summary>
    public Divergence? UpstreamDivergence { get; init; }

    /// <summary>
    /// Current HEAD vs the remote default branch
    /// (for example <c>feature/search</c> vs <c>origin/main</c>).
    /// </summary>
    public Divergence? DefaultBranchDivergence { get; init; }

    public bool MergeInProgress { get; init; }

    public bool RebaseInProgress { get; init; }

    public bool CherryPickInProgress { get; init; }

    public DateTimeOffset InspectedAt { get; init; }
}
