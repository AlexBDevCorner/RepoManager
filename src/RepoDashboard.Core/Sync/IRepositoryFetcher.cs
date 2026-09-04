using RepoDashboard.Core.Models;

namespace RepoDashboard.Core.Sync;

/// <summary>
/// Fetches remote state for one repository (<c>git fetch --prune</c>).
/// Observation of the result (re-inspection) is the caller's
/// responsibility — the fetcher itself only talks to the network.
/// </summary>
public interface IRepositoryFetcher
{
    Task<RepositoryOperationResult> FetchAsync(
        RepositoryConfiguration repository,
        CancellationToken cancellationToken);
}
