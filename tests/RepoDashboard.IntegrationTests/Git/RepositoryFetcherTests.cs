using FluentAssertions;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;
using RepoDashboard.Infrastructure.Git;

namespace RepoDashboard.IntegrationTests.Git;

public sealed class RepositoryFetcherTests
{
    private readonly IGitCommandRunner _git = new GitCommandRunner();

    private static RepositoryConfiguration ConfigFor(
        string path,
        string preferredRemote = "origin") =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Path = path,
            PreferredRemote = preferredRemote
        };

    private async Task RunAsync(string workingDirectory, params string[] arguments)
    {
        var result = await _git.ExecuteAsync(
            workingDirectory, arguments, CancellationToken.None);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} in '{workingDirectory}' " +
                $"failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }
    }

    [Fact]
    public async Task FetchAsync_NewRemoteCommits_UpdatesRemoteTrackingRefs()
    {
        // Arrange: remote/main = A, both clones at A, then remote moves to A-B-C.
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repoA = await factory.CloneAsync(remote, "repo-a");
        await factory.CommitFileAsync(repoA, "a.txt", "A", "Commit A");
        await factory.PushAsync(repoA);

        var repoB = await factory.CloneAsync(remote, "repo-b");

        await factory.CommitFileAsync(repoA, "b.txt", "B", "Commit B");
        await factory.CommitFileAsync(repoA, "c.txt", "C", "Commit C");
        await factory.PushAsync(repoA);

        IDivergenceCalculator calculator = new DivergenceCalculator(_git);
        var stale = await calculator.CalculateAsync(
            repoB, "HEAD", "origin/main", CancellationToken.None);
        stale.Should().Be(new Divergence(Ahead: 0, Behind: 0));

        IRepositoryFetcher sut = new RepositoryFetcher(_git);

        // Act
        var result = await sut.FetchAsync(
            ConfigFor(repoB), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Operation.Should().Be(RepositoryOperationType.Fetch);
        result.ExitCode.Should().Be(0);

        var fresh = await calculator.CalculateAsync(
            repoB, "HEAD", "origin/main", CancellationToken.None);
        fresh.Should().Be(new Divergence(Ahead: 0, Behind: 2));
    }

    [Fact]
    public async Task FetchAsync_PreferredRemote_FetchesThatRemote()
    {
        // Arrange: repo tracks two remotes; only "upstream" advances.
        using var factory = new GitTestRepositoryFactory();
        var origin = await factory.CreateBareRepositoryAsync("origin.git");
        var upstream = await factory.CreateBareRepositoryAsync("upstream.git");

        var seed = await factory.CloneAsync(origin, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed);

        var repo = await factory.CloneAsync(origin, "repo");
        await RunAsync(repo, "remote", "add", "upstream", upstream);

        var other = await factory.CloneAsync(upstream, "other");
        await factory.CommitFileAsync(other, "up.txt", "U", "Upstream commit");
        await RunAsync(other, "push", "-u", "origin", "main");

        IRepositoryFetcher sut = new RepositoryFetcher(_git);

        // Act
        var result = await sut.FetchAsync(
            ConfigFor(repo, preferredRemote: "upstream"),
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        var showUpstream = await _git.ExecuteAsync(
            repo, ["show-ref", "--verify", "--quiet", "refs/remotes/upstream/main"],
            CancellationToken.None);
        showUpstream.Success.Should().BeTrue();
    }

    [Fact]
    public async Task FetchAsync_UnknownRemote_ReturnsFailure()
    {
        // Arrange
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repo = await factory.CloneAsync(remote, "repo");

        IRepositoryFetcher sut = new RepositoryFetcher(_git);

        // Act: no exception — failures are returned for batch collection.
        var result = await sut.FetchAsync(
            ConfigFor(repo, preferredRemote: "does-not-exist"),
            CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Operation.Should().Be(RepositoryOperationType.Fetch);
        result.ExitCode.Should().NotBe(0);
        result.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task FetchAsync_DeletedRemoteBranch_PrunesStaleRef()
    {
        // Arrange: origin/feature exists, repo-b knows it, then it is deleted.
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repoA = await factory.CloneAsync(remote, "repo-a");
        await factory.CommitFileAsync(repoA, "a.txt", "A", "Commit A");
        await factory.PushAsync(repoA);

        var repoB = await factory.CloneAsync(remote, "repo-b");

        await factory.CreateBranchAsync(repoA, "feature");
        await factory.CheckoutAsync(repoA, "feature");
        await factory.CommitFileAsync(repoA, "f.txt", "F", "Feature commit");
        await RunAsync(repoA, "push", "-u", "origin", "feature");
        await factory.FetchAsync(repoB);

        var knownBefore = await _git.ExecuteAsync(
            repoB, ["show-ref", "--verify", "--quiet", "refs/remotes/origin/feature"],
            CancellationToken.None);
        knownBefore.Success.Should().BeTrue("setup: repo-b must know origin/feature");

        await RunAsync(repoA, "push", "origin", "--delete", "feature");

        IRepositoryFetcher sut = new RepositoryFetcher(_git);

        // Act
        var result = await sut.FetchAsync(
            ConfigFor(repoB), CancellationToken.None);

        // Assert: --prune removed the stale remote-tracking ref.
        result.Success.Should().BeTrue();

        var knownAfter = await _git.ExecuteAsync(
            repoB, ["show-ref", "--verify", "--quiet", "refs/remotes/origin/feature"],
            CancellationToken.None);
        knownAfter.Success.Should().BeFalse("fetch --prune must drop the deleted branch");
    }
}
