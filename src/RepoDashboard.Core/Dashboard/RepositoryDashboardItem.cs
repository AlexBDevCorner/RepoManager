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

    /// <summary>
    /// Raw inspection failure for this repository, if its inspection threw.
    /// Null when inspection succeeded. A failed item keeps the repository
    /// visible (failed operations never drop siblings); its decision is
    /// <c>Unknown</c> and the message is intentionally unclassified —
    /// friendly Git-error classification is a later task.
    /// </summary>
    public string? InspectionError { get; init; }

    public DateTimeOffset? LastSuccessfulFetch { get; init; }
}
