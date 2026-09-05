using FluentAssertions;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;
using RepoDashboard.Infrastructure.Git;

namespace RepoDashboard.IntegrationTests.Git;

/// <summary>
/// Complete integration matrix (Task 45): all 14 ticket scenarios against
/// real git.exe via <see cref="GitTestRepositoryFactory"/>. Each scenario
/// builds its own throwaway remote + clones so tests stay independent.
/// </summary>
public sealed class RepositoryLifecycleMatrixTests
{
    private readonly IGitCommandRunner _git = new GitCommandRunner();

    private RepositoryInspector CreateInspector() =>
        new(_git, new DivergenceCalculator(_git));

    private RepositoryUpdater CreateUpdater() =>
        new(
            _git,
            new RepositoryFetcher(_git),
            CreateInspector(),
            new UpdateEligibilityClassifier());

    private static RepositoryConfiguration ConfigFor(string path) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Path = path,
            PreferredRemote = "origin"
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

    [Fact]
    public async Task Matrix01_UpToDate_Ahead0Behind0_AlreadyUpToDate()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed);
        var repo = await factory.CloneAsync(remote, "repo");

        var inspector = CreateInspector();
        var snapshot = await inspector.InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        snapshot.UpstreamDivergence.Should().Be(new Divergence(0, 0));

        var decision = new UpdateEligibilityClassifier()
            .Classify(ConfigFor(repo), snapshot);

        decision.Eligibility.Should().Be(UpdateEligibility.AlreadyUpToDate);
    }

    [Fact]
    public async Task Matrix02_Behind_Ahead0Behind2_CanFastForward()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repoA = await factory.CloneAsync(remote, "repo-a");
        await factory.CommitFileAsync(repoA, "a.txt", "A", "Commit A");
        await factory.PushAsync(repoA);
        var repoB = await factory.CloneAsync(remote, "repo-b");

        await factory.CommitFileAsync(repoA, "b.txt", "B", "Commit B");
        await factory.CommitFileAsync(repoA, "c.txt", "C", "Commit C");
        await factory.PushAsync(repoA);
        await factory.FetchAsync(repoB);

        var inspector = CreateInspector();
        var snapshot = await inspector.InspectAsync(
            ConfigFor(repoB), CancellationToken.None);

        snapshot.UpstreamDivergence.Should().Be(new Divergence(0, 2));

        var decision = new UpdateEligibilityClassifier()
            .Classify(ConfigFor(repoB), snapshot);

        decision.Eligibility.Should().Be(UpdateEligibility.CanFastForward);
    }

    [Fact]
    public async Task Matrix03_Ahead_Ahead1Behind0_Ahead()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed);
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.CommitFileAsync(repo, "b.txt", "B", "Local B");

        var snapshot = await CreateInspector().InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        snapshot.UpstreamDivergence.Should().Be(new Divergence(1, 0));

        new UpdateEligibilityClassifier()
            .Classify(ConfigFor(repo), snapshot)
            .Eligibility.Should().Be(UpdateEligibility.Ahead);
    }

    [Fact]
    public async Task Matrix04_Diverged_Ahead1Behind1_Diverged()
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
        await factory.FetchAsync(repoB);

        var snapshot = await CreateInspector().InspectAsync(
            ConfigFor(repoB), CancellationToken.None);

        snapshot.UpstreamDivergence.Should().Be(new Divergence(1, 1));

        new UpdateEligibilityClassifier()
            .Classify(ConfigFor(repoB), snapshot)
            .Eligibility.Should().Be(UpdateEligibility.Diverged);
    }

    [Fact]
    public async Task Matrix05_Dirty_ModifiedReadme_IsDirty_Dirty()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "README.md", "base", "Commit A");
        await factory.PushAsync(seed);
        var repo = await factory.CloneAsync(remote, "repo");

        await File.AppendAllTextAsync(
            Path.Combine(repo, "README.md"), "uncommitted");

        var snapshot = await CreateInspector().InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        snapshot.IsDirty.Should().BeTrue();

        new UpdateEligibilityClassifier()
            .Classify(ConfigFor(repo), snapshot)
            .Eligibility.Should().Be(UpdateEligibility.Dirty);
    }

    [Fact]
    public async Task Matrix06_Untracked_NewFile_IsDirty()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed);
        var repo = await factory.CloneAsync(remote, "repo");

        await File.WriteAllTextAsync(
            Path.Combine(repo, "new-file.txt"), "untracked");

        var snapshot = await CreateInspector().InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        snapshot.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task Matrix07_NoUpstream_LocalOnlyBranch_NoUpstream()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed);
        var repo = await factory.CloneAsync(remote, "repo");

        await factory.CreateBranchAsync(repo, "feature/local-only");
        await factory.CheckoutAsync(repo, "feature/local-only");

        var snapshot = await CreateInspector().InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        snapshot.UpstreamRef.Should().BeNull();

        new UpdateEligibilityClassifier()
            .Classify(ConfigFor(repo), snapshot)
            .Eligibility.Should().Be(UpdateEligibility.NoUpstream);
    }

    [Fact]
    public async Task Matrix08_DetachedHead_CheckoutSha_DetachedHead()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed);
        var repo = await factory.CloneAsync(remote, "repo");

        var sha = await HeadAsync(repo);
        await RunAsync(repo, "checkout", "--detach", sha);

        var snapshot = await CreateInspector().InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        snapshot.IsDetachedHead.Should().BeTrue();

        new UpdateEligibilityClassifier()
            .Classify(ConfigFor(repo), snapshot)
            .Eligibility.Should().Be(UpdateEligibility.DetachedHead);
    }

    [Fact]
    public async Task Matrix09_FastForwardUpdate_Behind2_PullsToZeroZero()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repoA = await factory.CloneAsync(remote, "repo-a");
        await factory.CommitFileAsync(repoA, "a.txt", "A", "Commit A");
        await factory.PushAsync(repoA);
        var repoB = await factory.CloneAsync(remote, "repo-b");
        await factory.CommitFileAsync(repoA, "b.txt", "B", "Commit B");
        await factory.CommitFileAsync(repoA, "c.txt", "C", "Commit C");
        await factory.PushAsync(repoA);

        var result = await CreateUpdater().UpdateAsync(
            ConfigFor(repoB), CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Updated);
        File.Exists(Path.Combine(repoB, "c.txt")).Should().BeTrue();
        result.FinalSnapshot?.UpstreamDivergence.Should().Be(new Divergence(0, 0));
    }

    [Fact]
    public async Task Matrix10_DirtyUpdate_Behind2Dirty_SkippedFilesUnchanged()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repoA = await factory.CloneAsync(remote, "repo-a");
        await factory.CommitFileAsync(repoA, "a.txt", "A", "Commit A");
        await factory.PushAsync(repoA);
        var repoB = await factory.CloneAsync(remote, "repo-b");
        await factory.CommitFileAsync(repoA, "b.txt", "B", "Commit B");
        await factory.CommitFileAsync(repoA, "c.txt", "C", "Commit C");
        await factory.PushAsync(repoA);

        var dirtyPath = Path.Combine(repoB, "dirty.txt");
        await File.WriteAllTextAsync(dirtyPath, "dirty");

        var result = await CreateUpdater().UpdateAsync(
            ConfigFor(repoB), CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Skipped);
        result.Decision?.Eligibility.Should().Be(UpdateEligibility.Dirty);
        File.ReadAllText(dirtyPath).Should().Be("dirty");
        File.Exists(Path.Combine(repoB, "c.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Matrix11_DivergedUpdate_SkippedWithoutMergeOrReset()
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

        var result = await CreateUpdater().UpdateAsync(
            ConfigFor(repoB), CancellationToken.None);

        result.Outcome.Should().Be(RepositoryUpdateOutcome.Skipped);
        result.Decision?.Eligibility.Should().Be(UpdateEligibility.Diverged);
        (await HeadAsync(repoB)).Should().Be(headBefore);
    }

    [Fact]
    public async Task Matrix12_MainDefault_RemoteHeadMain_ResolvesMain()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed);
        var repo = await factory.CloneAsync(remote, "repo");

        var snapshot = await CreateInspector().InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        snapshot.DefaultRemoteBranch.Should().Be("main");
    }

    [Fact]
    public async Task Matrix13_MasterDefault_RemoteHeadMaster_ResolvesMaster()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = Path.Combine(factory.RootPath, "remote.git");
        await RunAsync(factory.RootPath, "init", "--bare", "-b", "master", remote);

        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed, "master");
        var repo = await factory.CloneAsync(remote, "repo");

        var snapshot = await CreateInspector().InspectAsync(
            ConfigFor(repo), CancellationToken.None);

        snapshot.CurrentBranch.Should().Be("master");
        snapshot.DefaultRemoteBranch.Should().Be("master");
    }

    [Fact]
    public async Task Matrix14_MissingFolder_ReportsRepositoryMissing_AppContinues()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var seed = await factory.CloneAsync(remote, "seed");
        await factory.CommitFileAsync(seed, "a.txt", "A", "Commit A");
        await factory.PushAsync(seed);
        var repo = await factory.CloneAsync(remote, "repo");

        var config = ConfigFor(repo);
        TestDirectories.DeleteRecursively(repo);

        var snapshot = await CreateInspector().InspectAsync(
            config, CancellationToken.None);

        snapshot.DirectoryExists.Should().BeFalse();

        new UpdateEligibilityClassifier()
            .Classify(config, snapshot)
            .Eligibility.Should().Be(UpdateEligibility.RepositoryMissing);
    }
}
