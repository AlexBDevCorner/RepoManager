using RepoDashboard.Core.Models;

namespace RepoDashboard.Core.Repositories;

public interface IRepositoryConfigurationStore
{
    Task<IReadOnlyList<RepositoryConfiguration>> LoadAsync(
        CancellationToken cancellationToken);

    Task SaveAsync(
        IReadOnlyCollection<RepositoryConfiguration> repositories,
        CancellationToken cancellationToken);
}
