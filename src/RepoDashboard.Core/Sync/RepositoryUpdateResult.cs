using RepoDashboard.Core.Models;

namespace RepoDashboard.Core.Sync;

/// <summary>
/// The outcome of a single safe-update attempt
/// (fetch → inspect → classify → conditional pull → reinspect).
/// <c>Skipped</c> carries the classifier's explanation so the application
/// can always answer why a repository was not updated; <c>Failed</c> is a
/// safe failure — never a fallback to merge / rebase / reset / stash.
/// </summary>
public sealed record RepositoryUpdateResult
{
    public required Guid RepositoryId { get; init; }

    public required RepositoryUpdateOutcome Outcome { get; init; }

    public required string Message { get; init; }

    /// <summary>
    /// The classification that drove the decision to pull or skip.
    /// Null when the attempt never reached classification
    /// (fetch or inspection failed first).
    /// </summary>
    public UpdateDecision? Decision { get; init; }

    /// <summary>
    /// The freshest snapshot observed during the attempt: post-pull for
    /// <c>Updated</c>, post-fetch for <c>Skipped</c>, best-effort for
    /// <c>Failed</c>. Null only when no inspection succeeded.
    /// Lets callers render the row without inspecting a third time.
    /// </summary>
    public RepositorySnapshot? FinalSnapshot { get; init; }

    /// <summary>
    /// The fetch step's result. Null only when the fetcher threw
    /// unexpectedly instead of returning a failure.
    /// </summary>
    public RepositoryOperationResult? FetchResult { get; init; }
}
