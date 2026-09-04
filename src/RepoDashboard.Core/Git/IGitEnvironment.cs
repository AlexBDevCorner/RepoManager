namespace RepoDashboard.Core.Git;

public interface IGitEnvironment
{
    Task<GitEnvironmentInfo> CheckAsync(
        CancellationToken cancellationToken = default);
}
