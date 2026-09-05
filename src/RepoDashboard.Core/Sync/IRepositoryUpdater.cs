using RepoDashboard.Core.Models;

namespace RepoDashboard.Core.Sync;

/// <summary>
/// Single entry point for safe automatic updates of one repository.
/// Runs fetch → inspect → classify → conditional pull → reinspect.
/// The updater itself controls exactly which Git operations are allowed:
/// <c>fetch --prune</c> plus, only when classified as fast-forwardable,
/// <c>pull --ff-only --no-rebase</c>. Callers cannot inject arbitrary
/// Git commands.
/// <para/>
/// The updater holds no locks: it is a single-repository algorithm
/// primitive. Callers performing concurrent operations must serialize
/// per-repository execution themselves — <c>RepositoryDashboardService</c>
/// is the application's single orchestration point and holds its shared
/// per-repository lock and global semaphore around this call, so a fetch
/// and an update on the same repository can never overlap.
/// </summary>
public interface IRepositoryUpdater
{
    Task<RepositoryUpdateResult> UpdateAsync(
        RepositoryConfiguration repository,
        CancellationToken cancellationToken);
}
