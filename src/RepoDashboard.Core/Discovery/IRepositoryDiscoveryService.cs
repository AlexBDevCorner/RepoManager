namespace RepoDashboard.Core.Discovery;

/// <summary>
/// Finds Git repository roots under a parent folder (Task 40).
/// Depth-limited, skips hidden folders, and never descends into a
/// discovered repository. Discovery never modifies configuration —
/// the caller shows the results for explicit user confirmation
/// before adding anything.
/// </summary>
public interface IRepositoryDiscoveryService
{
    /// <param name="rootPath">Folder to scan (e.g. C:\Source\Repos).</param>
    /// <param name="maxDepth">
    /// How many levels below <paramref name="rootPath"/> to descend.
    /// Default 3 per the ticket. Must be &gt;= 0.
    /// </param>
    /// <exception cref="DirectoryNotFoundException">Root does not exist.</exception>
    Task<IReadOnlyList<DiscoveredRepository>> DiscoverAsync(
        string rootPath,
        int maxDepth = 3,
        CancellationToken cancellationToken = default);
}
