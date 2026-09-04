using RepoDashboard.Core.Models;

namespace RepoDashboard.Core.Git;

public interface IDivergenceCalculator
{
    /// <summary>
    /// Computes how far <paramref name="leftRef"/> is ahead of / behind
    /// <paramref name="rightRef"/> via
    /// <c>git rev-list --left-right --count left...right</c>.
    /// Returns null when either ref cannot be resolved
    /// (unknown divergence, not an error).
    /// </summary>
    Task<Divergence?> CalculateAsync(
        string repositoryPath,
        string leftRef,
        string rightRef,
        CancellationToken cancellationToken);
}
