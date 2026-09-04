using RepoDashboard.Core.Models;
using RepoDashboard.Core.Repositories;

namespace RepoDashboard.Core.Git;

/// <summary>
/// Read-only understanding of a repository. Implementations must never
/// <c>fetch</c> / <c>pull</c> / <c>checkout</c> / <c>merge</c> / <c>rebase</c> /
/// <c>reset</c> / <c>stash</c> — observation only, no network or mutation.
/// </summary>
public interface IRepositoryInspector
{
    Task<RepositorySnapshot> InspectAsync(
        RepositoryConfiguration repository,
        CancellationToken cancellationToken);
}
