using RepoDashboard.Core.Sync;

namespace RepoDashboard.App.ViewModels;

/// <summary>
/// Short human-readable labels for update eligibility, shared by the batch
/// summary and the compact per-row activity text. Full explanations stay in
/// the details area; these labels keep the table narrow.
/// </summary>
internal static class UpdateEligibilityLabels
{
    internal static string Short(UpdateEligibility eligibility) =>
        eligibility switch
        {
            UpdateEligibility.AlreadyUpToDate => "already current",
            UpdateEligibility.Ahead => "ahead",
            UpdateEligibility.Dirty => "dirty",
            UpdateEligibility.Diverged => "diverged",
            UpdateEligibility.NoUpstream => "no upstream",
            UpdateEligibility.UpstreamUsesDifferentRemote => "different remote",
            UpdateEligibility.DetachedHead => "detached",
            UpdateEligibility.OperationInProgress => "busy",
            UpdateEligibility.RepositoryMissing => "missing",
            UpdateEligibility.InvalidRepository => "invalid",
            UpdateEligibility.CanFastForward => "can fast-forward",
            _ => "unknown",
        };
}
