namespace RepoDashboard.Core.State;

/// <summary>
/// Persists operational state that Git itself does not know, currently the
/// last-successful-fetch timestamp per repository. This is application
/// metadata: it lives in <c>state.json</c>, never in
/// <c>repositories.json</c>, so user configuration stays clean of
/// operational state. Only successful fetches are recorded; failed
/// fetches leave the stored timestamp untouched.
/// </summary>
public interface IOperationStateStore
{
    Task<IReadOnlyDictionary<Guid, DateTimeOffset>> LoadAsync(
        CancellationToken cancellationToken);

    Task SaveAsync(
        IReadOnlyDictionary<Guid, DateTimeOffset> lastSuccessfulFetch,
        CancellationToken cancellationToken);
}
