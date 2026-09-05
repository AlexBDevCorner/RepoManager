namespace RepoDashboard.Core.Lifetime;

/// <summary>
/// Default <see cref="IApplicationShutdown"/> backed by a
/// <see cref="CancellationTokenSource"/> cancelled once on shutdown.
/// Registered as a singleton so the view model, command runner, updater
/// and dashboard service all observe the same shutdown signal.
/// </summary>
public sealed class ApplicationShutdown : IApplicationShutdown, IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();

    /// <inheritdoc />
    public CancellationToken ShutdownToken => _shutdown.Token;

    /// <inheritdoc />
    public void NotifyShuttingDown()
    {
        try
        {
            _shutdown.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed during host teardown.
        }
    }

    public void Dispose() => _shutdown.Dispose();
}
