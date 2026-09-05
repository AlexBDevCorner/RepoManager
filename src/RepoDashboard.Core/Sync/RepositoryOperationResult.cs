namespace RepoDashboard.Core.Sync;

/// <summary>
/// The outcome of a single repository operation (<c>fetch</c>, <c>update</c>,
/// <c>refresh</c>). Failures are returned, not thrown, so callers can
/// collect every result without aborting batch operations.
/// </summary>
public sealed record RepositoryOperationResult
{
    public required bool Success { get; init; }

    public required RepositoryOperationType Operation { get; init; }

    public required string Message { get; init; }

    public string? RawOutput { get; init; }

    /// <summary>
    /// Friendly hint for known Git failure patterns (Task 42), or null
    /// when the output matches nothing known. Raw output in
    /// <see cref="RawOutput"/>/<see cref="Message"/> is always preserved —
    /// the hint is guidance alongside it, never a replacement.
    /// </summary>
    public string? FriendlyHint { get; init; }

    public int? ExitCode { get; init; }

    public TimeSpan Duration { get; init; }
}
