using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.Core.Dashboard;

/// <summary>
/// Everything the UI needs for one repository row.
/// The UI must not independently combine configuration / snapshot /
/// eligibility — that is the application's responsibility.
/// </summary>
public sealed record RepositoryDashboardItem
{
    public required RepositoryConfiguration Configuration { get; init; }

    public required RepositorySnapshot Snapshot { get; init; }

    public required UpdateDecision UpdateDecision { get; init; }

    public DateTimeOffset? LastSuccessfulFetch { get; init; }
}
