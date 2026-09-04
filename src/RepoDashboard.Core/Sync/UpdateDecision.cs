namespace RepoDashboard.Core.Sync;

/// <summary>
/// The result of classifying a repository for safe update.
/// <c>CanUpdate</c> is true only for <c>CanFastForward</c>;
/// every refusal carries a human-readable <c>Explanation</c>.
/// </summary>
public sealed record UpdateDecision(
    UpdateEligibility Eligibility,
    string Explanation)
{
    public bool CanUpdate =>
        Eligibility == UpdateEligibility.CanFastForward;
}
