using FluentAssertions;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;
using RepoDashboard.Infrastructure.Git;

namespace RepoDashboard.IntegrationTests.Git;

public sealed class RepositoryUpdaterTests
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

    private RepositoryUpdater CreateSut()
    {
        var fetcher = new RepositoryFetcher(_git);
        var inspector = new RepositoryInspector(
            _git, new DivergenceCalculator(_git));

        return new RepositoryUpdater(
            _git, fetcher, inspector, new UpdateEligibilityClassifier());
    }

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

    private async Task<string> HeadAsync(string repositoryPath)
    {
        var result = await _git.ExecuteAsync(
            repositoryPath, ["rev-parse", "HEAD"], CancellationToken.None);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"git rev-parse HEAD in '{repositoryPath}' failed: {result.StandardError.Trim()}");
        }

        return result.StandardOutput.Trim();
    }

    private async Task<Divergence?> DivergenceAsync(string repositoryPath)
    {
        IDivergenceCalculator calculator = new DivergenceCalculator(_git);

        return await calculator.CalculateAsync(
            repositoryPath, "HEAD", "origin/main", CancellationToken.None);
    }

    /// <summary>
    /// remote/main = A, repo-b cloned at A, remote advanced to A-B.
    /// Returns (repoB path, remote path is owned by the factory).
    /// </summary>
    private static async Task<(GitTestRepositoryFactory Factory, string RepoB)> BehindSetupAsync()
    {
        var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repoA = await factory.CloneAsync(remote, "repo-a");
        await factory.CommitFileAsync(repoA, "a.txt", "A", "Commit A");
        await factory.PushAsync(repoA);

        var repoB = await factory.CloneAsync(remote, "repo-b");

        await factory.CommitFileAsync(repoA, "b.txt", "B", "Commit B");
        await factory.PushAsync(repoA);

        return (factory, repoB);
    }

    [Fact]
    public async Task UpdateAsync_BehindOnly_UpdatesToRemoteTip()
    {
        var setup = await BehindSetupAsync();
        using var factory = setup.Factory;
        var repoB = setup.RepoB;
        var sut = CreateSut();

        var result = await sut.UpdateAsync(ConfigFor(repoB), CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Updated);
        result.Decision?.Eligibility.Should().Be(UpdateEligibility.CanFastForward);
        result.FetchResult?.Success.Should().BeTrue();
        result.Message.Should().Contain("Fast-forward");

        // The working tree actually moved: file arrived, divergence is 0,0.
        File.Exists(Path.Combine(repoB, "b.txt")).Should().BeTrue();
        (await DivergenceAsync(repoB)).Should().Be(new Divergence(0, 0));
        result.FinalSnapshot?.UpstreamDivergence.Should().Be(new Divergence(0, 0));
    }

    [Fact]
    public async Task UpdateAsync_AlreadyUpToDate_SkipsWithoutPulling()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repoA = await factory.CloneAsync(remote, "repo-a");
        await factory.CommitFileAsync(repoA, "a.txt", "A", "Commit A");
        await factory.PushAsync(repoA);
        var repoB = await factory.CloneAsync(remote, "repo-b");
        var sut = CreateSut();

        var result = await sut.UpdateAsync(ConfigFor(repoB), CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Skipped);
        result.Decision?.Eligibility.Should().Be(UpdateEligibility.AlreadyUpToDate);
        result.Message.Should().Contain("up to date");
    }

    [Fact]
    public async Task UpdateAsync_DirtyTree_SkipsAndLeavesWorktreeAlone()
    {
        var setup = await BehindSetupAsync();
        using var factory = setup.Factory;
        var repoB = setup.RepoB;
        await File.WriteAllTextAsync(
            Path.Combine(repoB, "uncommitted.txt"), "dirty");
        var sut = CreateSut();

        var result = await sut.UpdateAsync(ConfigFor(repoB), CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Skipped);
        result.Decision?.Eligibility.Should().Be(UpdateEligibility.Dirty);

        // Nothing was pulled, stashed or reset: dirt is intact, tip is stale.
        File.ReadAllText(Path.Combine(repoB, "uncommitted.txt")).Should().Be("dirty");
        File.Exists(Path.Combine(repoB, "b.txt")).Should().BeFalse();
        (await DivergenceAsync(repoB)).Should().Be(new Divergence(0, 1));
    }

    [Fact]
    public async Task UpdateAsync_Diverged_SkipsWithoutTouchingHead()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repoA = await factory.CloneAsync(remote, "repo-a");
        await factory.CommitFileAsync(repoA, "a.txt", "A", "Commit A");
        await factory.PushAsync(repoA);
        var repoB = await factory.CloneAsync(remote, "repo-b");

        await factory.CommitFileAsync(repoB, "local.txt", "L", "Local commit");
        await factory.CommitFileAsync(repoA, "remote.txt", "R", "Remote commit");
        await factory.PushAsync(repoA);

        var headBefore = await HeadAsync(repoB);
        var sut = CreateSut();

        var result = await sut.UpdateAsync(ConfigFor(repoB), CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Skipped);
        result.Decision?.Eligibility.Should().Be(UpdateEligibility.Diverged);
        (await HeadAsync(repoB)).Should().Be(headBefore);
    }

    [Fact]
    public async Task UpdateAsync_AheadOnly_SkipsWithoutPulling()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repoA = await factory.CloneAsync(remote, "repo-a");
        await factory.CommitFileAsync(repoA, "a.txt", "A", "Commit A");
        await factory.PushAsync(repoA);
        var repoB = await factory.CloneAsync(remote, "repo-b");
        await factory.CommitFileAsync(repoB, "local.txt", "L", "Local commit");

        var headBefore = await HeadAsync(repoB);
        var sut = CreateSut();

        var result = await sut.UpdateAsync(ConfigFor(repoB), CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Skipped);
        result.Decision?.Eligibility.Should().Be(UpdateEligibility.Ahead);
        (await HeadAsync(repoB)).Should().Be(headBefore);
    }

    [Fact]
    public async Task UpdateAsync_NoUpstream_Skips()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repoA = await factory.CloneAsync(remote, "repo-a");
        await factory.CommitFileAsync(repoA, "a.txt", "A", "Commit A");
        await factory.PushAsync(repoA);
        var repoB = await factory.CloneAsync(remote, "repo-b");

        await factory.CreateBranchAsync(repoB, "local-only");
        await factory.CheckoutAsync(repoB, "local-only");
        await factory.CommitFileAsync(repoB, "local.txt", "L", "Local commit");

        var sut = CreateSut();

        var result = await sut.UpdateAsync(ConfigFor(repoB), CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Skipped);
        result.Decision?.Eligibility.Should().Be(UpdateEligibility.NoUpstream);
    }

    [Fact]
    public async Task UpdateAsync_DetachedHead_SkipsWithoutTouchingHead()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repoA = await factory.CloneAsync(remote, "repo-a");
        await factory.CommitFileAsync(repoA, "a.txt", "A", "Commit A");
        await factory.PushAsync(repoA);
        var repoB = await factory.CloneAsync(remote, "repo-b");

        await RunAsync(repoB, "checkout", "--detach");
        var headBefore = await HeadAsync(repoB);
        var sut = CreateSut();

        var result = await sut.UpdateAsync(ConfigFor(repoB), CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Skipped);
        result.Decision?.Eligibility.Should().Be(UpdateEligibility.DetachedHead);
        (await HeadAsync(repoB)).Should().Be(headBefore);
    }

    [Fact]
    public async Task UpdateAsync_UnknownRemote_FailsWithoutPulling()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed);
        var repo = await factory.CloneAsync(remote, "repo");

        var headBefore = await HeadAsync(repo);
        var sut = CreateSut();

        var result = await sut.UpdateAsync(
            ConfigFor(repo, preferredRemote: "does-not-exist"),
            CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Failed);
        result.Decision.Should().BeNull();
        result.FetchResult?.Success.Should().BeFalse();
        (await HeadAsync(repo)).Should().Be(headBefore);
    }
}
