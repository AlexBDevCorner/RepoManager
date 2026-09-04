using FluentAssertions;
using RepoDashboard.Core.Git;
using RepoDashboard.Infrastructure.Git;

namespace RepoDashboard.IntegrationTests.Git;

public sealed class DivergenceCalculatorTests
{
    [Fact]
    public async Task CalculateAsync_UpToDate_ReturnsZeroZero()
    {
        // Arrange: remote/main = A, clone at A.
        using var factory = new GitTestRepositoryFactory();
        IDivergenceCalculator calculator = new DivergenceCalculator(new GitCommandRunner());

        var remote = await factory.CreateBareRepositoryAsync();
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.CommitFileAsync(repo, "a.txt", "A", "Commit A");
        await factory.PushAsync(repo);

        // Act
        var divergence = await calculator.CalculateAsync(
            repo, "HEAD", "origin/main", CancellationToken.None);

        // Assert
        divergence.Should().Be(new Core.Models.Divergence(Ahead: 0, Behind: 0));
    }

    [Fact]
    public async Task CalculateAsync_AheadOnly_ReturnsAheadZeroBehind()
    {
        // Arrange: local has 3 commits the remote does not.
        using var factory = new GitTestRepositoryFactory();
        IDivergenceCalculator calculator = new DivergenceCalculator(new GitCommandRunner());

        var remote = await factory.CreateBareRepositoryAsync();
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.CommitFileAsync(repo, "a.txt", "A", "Commit A");
        await factory.PushAsync(repo);
        await factory.CommitFileAsync(repo, "b.txt", "B", "Commit B");
        await factory.CommitFileAsync(repo, "c.txt", "C", "Commit C");
        await factory.CommitFileAsync(repo, "d.txt", "D", "Commit D");

        // Act
        var divergence = await calculator.CalculateAsync(
            repo, "HEAD", "origin/main", CancellationToken.None);

        // Assert
        divergence.Should().Be(new Core.Models.Divergence(Ahead: 3, Behind: 0));
    }

    [Fact]
    public async Task CalculateAsync_BehindOnly_ReturnsZeroAheadBehind()
    {
        // Arrange: remote moved 3 commits past the local HEAD.
        using var factory = new GitTestRepositoryFactory();
        IDivergenceCalculator calculator = new DivergenceCalculator(new GitCommandRunner());

        var remote = await factory.CreateBareRepositoryAsync();
        var repoA = await factory.CloneAsync(remote, "repo-a");
        await factory.CommitFileAsync(repoA, "a.txt", "A", "Commit A");
        await factory.PushAsync(repoA);

        var repoB = await factory.CloneAsync(remote, "repo-b");

        await factory.CommitFileAsync(repoA, "b.txt", "B", "Commit B");
        await factory.CommitFileAsync(repoA, "c.txt", "C", "Commit C");
        await factory.CommitFileAsync(repoA, "d.txt", "D", "Commit D");
        await factory.PushAsync(repoA);
        await factory.FetchAsync(repoB);

        // Act
        var divergence = await calculator.CalculateAsync(
            repoB, "HEAD", "origin/main", CancellationToken.None);

        // Assert
        divergence.Should().Be(new Core.Models.Divergence(Ahead: 0, Behind: 3));
    }

    [Fact]
    public async Task CalculateAsync_Diverged_ReturnsAheadAndBehind()
    {
        // Arrange: 2 local-only commits, 4 remote-only commits.
        using var factory = new GitTestRepositoryFactory();
        IDivergenceCalculator calculator = new DivergenceCalculator(new GitCommandRunner());

        var remote = await factory.CreateBareRepositoryAsync();
        var repoA = await factory.CloneAsync(remote, "repo-a");
        await factory.CommitFileAsync(repoA, "a.txt", "A", "Commit A");
        await factory.PushAsync(repoA);

        var repoB = await factory.CloneAsync(remote, "repo-b");

        await factory.CommitFileAsync(repoB, "local-1.txt", "L1", "Local 1");
        await factory.CommitFileAsync(repoB, "local-2.txt", "L2", "Local 2");

        await factory.CommitFileAsync(repoA, "remote-1.txt", "R1", "Remote 1");
        await factory.CommitFileAsync(repoA, "remote-2.txt", "R2", "Remote 2");
        await factory.CommitFileAsync(repoA, "remote-3.txt", "R3", "Remote 3");
        await factory.CommitFileAsync(repoA, "remote-4.txt", "R4", "Remote 4");
        await factory.PushAsync(repoA);
        await factory.FetchAsync(repoB);

        // Act
        var divergence = await calculator.CalculateAsync(
            repoB, "HEAD", "origin/main", CancellationToken.None);

        // Assert
        divergence.Should().Be(new Core.Models.Divergence(Ahead: 2, Behind: 4));
    }

    [Fact]
    public async Task CalculateAsync_UnknownRef_ReturnsNull()
    {
        // Arrange
        using var factory = new GitTestRepositoryFactory();
        IDivergenceCalculator calculator = new DivergenceCalculator(new GitCommandRunner());

        var remote = await factory.CreateBareRepositoryAsync();
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.CommitFileAsync(repo, "a.txt", "A", "Commit A");

        // Act: origin/main was never fetched — unknown, not an error.
        var divergence = await calculator.CalculateAsync(
            repo, "HEAD", "origin/main", CancellationToken.None);

        // Assert
        divergence.Should().BeNull();
    }
}
