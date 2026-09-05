namespace RepoDashboard.App.ViewModels;

/// <summary>
/// Presentation-only state of one dashboard row, independent of Git state.
/// Git state (<c>UpdateStatus</c> / <c>Explanation</c>) says what Git looks
/// like; activity says what the application is doing (or last did) with the
/// row. Rendered inline in the table — never as modal dialogs — so a batch
/// operation shows per-repository progress like "Store — Fetching..." or
/// "Search — Skipped — working tree dirty".
/// </summary>
public enum RepositoryActivity
{
    Idle,
    Refreshing,
    Fetching,
    Updating,
    Completed,
    Failed,
    Skipped
}
