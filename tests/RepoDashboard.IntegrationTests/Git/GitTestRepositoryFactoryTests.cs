using FluentAssertions;
using RepoDashboard.Core.Git;
using RepoDashboard.Infrastructure.Git;

namespace RepoDashboard.IntegrationTests.Git;

public sealed class GitTestRepositoryFactoryTests
{
    [Fact]
    public async Task CreateTwoClones_CommitPushFetch_SecondCloneIsBehind()
    {
        // Arrange: remote/main = A with both clones at A.
        using var factory = new GitTestRepositoryFactory();
        IGitCommandRunner git = new GitCommandRunner();

        var remote = await factory.CreateBareRepositoryAsync();
        var repoA = await factory.CloneAsync(remote, "repo-a");
        await factory.CommitFileAsync(repoA, "a.txt", "A", "Commit A");
        await factory.PushAsync(repoA);

        var repoB = await factory.CloneAsync(remote, "repo-b");

        // Act: repo-a moves remote/main to A-B; repo-b fetches.
        await factory.CommitFileAsync(repoA, "b.txt", "B", "Commit B");
        await factory.PushAsync(repoA);
        await factory.FetchAsync(repoB);

        var behind = await git.ExecuteAsync(
            repoB, ["rev-list", "--count", "HEAD..origin/main"]);
        var ahead = await git.ExecuteAsync(
            repoB, ["rev-list", "--count", "origin/main..HEAD"]);

        // Assert: Ahead 0, Behind 1.
        behind.Success.Should().BeTrue();
        behind.StandardOutput.Trim().Should().Be("1");
        ahead.Success.Should().BeTrue();
        ahead.StandardOutput.Trim().Should().Be("0");
    }
}
