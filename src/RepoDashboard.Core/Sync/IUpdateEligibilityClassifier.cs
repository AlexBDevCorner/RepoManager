using RepoDashboard.Core.Models;

namespace RepoDashboard.Core.Sync;

/// <summary>
/// Pure decision logic for safe updates. Implementations perform no IO
/// and never execute Git — they only classify a snapshot against configuration.
/// </summary>
public interface IUpdateEligibilityClassifier
{
    UpdateDecision Classify(
        RepositoryConfiguration configuration,
        RepositorySnapshot snapshot);
}
