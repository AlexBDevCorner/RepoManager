namespace RepoDashboard.Core.Sync;

/// <summary>
/// Whether a repository can be safely fast-forwarded,
/// and if not, why. Produced by <c>IUpdateEligibilityClassifier</c>
/// without performing any Git mutation or IO.
/// </summary>
public enum UpdateEligibility
{
    CanFastForward,

    AlreadyUpToDate,

    Ahead,

    Diverged,

    Dirty,

    NoUpstream,

    DetachedHead,

    OperationInProgress,

    RepositoryMissing,

    InvalidRepository,

    UpstreamUsesDifferentRemote,

    Unknown
}
