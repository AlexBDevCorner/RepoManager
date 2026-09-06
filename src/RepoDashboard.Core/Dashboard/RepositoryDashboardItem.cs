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

    /// <summary>
    /// Raw fetch failure for this repository, if the last fetch attempt
    /// failed. Null when no fetch was attempted or the last fetch succeeded.
    /// A fetch failure never hides local state: the snapshot and decision
    /// still describe the last successful inspection.
    /// </summary>
    public string? FetchError { get; init; }

    /// <summary>
    /// The last update attempt for this repository, if one was made.
    /// Null when no update was attempted. Carries the outcome
    /// (<c>Updated</c> / <c>Skipped</c> / <c>Failed</c>) with its
    /// human-readable reason.
    /// </summary>
    public RepositoryUpdateResult? UpdateResult { get; init; }

    /// <summary>
    /// Which operation produced this item, if any. Load/Add carry no
    /// operation (<c>null</c>): a historical <c>LastSuccessfulFetch</c>
    /// alone must never be presented as "the last operation was a fetch".
    /// </summary>
    public RepositoryOperationType? LastOperation { get; init; }

    /// <summary>
    /// Friendly hint for the current error (Task 42), or null when there
    /// is no error or the error matches no known pattern. The raw
    /// diagnostics stay in <c>FetchError</c>/<c>InspectionError</c>/
    /// <c>UpdateResult.Message</c> — this is guidance alongside them.
    /// </summary>
    public string? FriendlyHint { get; init; }

    public DateTimeOffset? LastSuccessfulFetch { get; init; }
}
