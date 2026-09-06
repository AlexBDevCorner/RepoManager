namespace RepoDashboard.Core.Lifetime;

/// <summary>
/// Application shutdown signal (Task 44).
/// Distinguishes <b>user Cancel</b> (per-operation, safe to ignore after a
/// mutation commits) from <b>application shutdown</b> (must always terminate
/// in-flight <c>git.exe</c> processes so none are orphaned).
/// Post-commit work (final re-inspection after a successful pull) observes
/// only <see cref="ShutdownToken"/>: user cancellation cannot hide a
/// committed update, while shutdown still kills the Git processes.
/// Named to avoid colliding with
/// <c>Microsoft.Extensions.Hosting.IApplicationLifetime</c>.
/// </summary>
public interface IApplicationShutdown
{
    /// <summary>
    /// Cancelled once when the application begins shutting down.
    /// Never cancelled by the Cancel button.
    /// </summary>
    CancellationToken ShutdownToken { get; }

    /// <summary>
    /// Signals application shutdown. Safe to call multiple times.
    /// </summary>
    void NotifyShuttingDown();
}
