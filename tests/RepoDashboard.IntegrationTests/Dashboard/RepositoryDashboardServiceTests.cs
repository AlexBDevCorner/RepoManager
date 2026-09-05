using FluentAssertions;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Sync;
using RepoDashboard.Infrastructure.Configuration;
using RepoDashboard.Infrastructure.Git;
using RepoDashboard.IntegrationTests.Git;

namespace RepoDashboard.IntegrationTests.Dashboard;

public sealed class RepositoryDashboardServiceTests
{
    private static RepositoryDashboardService CreateSut(string storePath)
    {
        var git = new GitCommandRunner();
        var inspector = new RepositoryInspector(
            git,
            new DivergenceCalculator(git));
        var fetcher = new RepositoryFetcher(git);
        var classifier = new UpdateEligibilityClassifier();

        return new(
            new JsonRepositoryConfigurationStore(storePath),
            inspector,
            classifier,
            fetcher,
            new RepositoryUpdater(git, fetcher, inspector, classifier));
    }

    private static string StorePath(GitTestRepositoryFactory factory) =>
        Path.Combine(factory.RootPath, "config", "repositories.json");

    [Fact]
    public async Task AddAsync_ThenLoadWithNewInstance_PersistsAcrossRestart()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.CommitFileAsync(repo, "a.txt", "A", "Commit A");
        await factory.PushAsync(repo);

        var storePath = StorePath(factory);
        var added = await CreateSut(storePath)
            .AddAsync(repo, CancellationToken.None);

        added.Configuration.Name.Should().Be("repo");
        added.Snapshot.CurrentBranch.Should().Be("main");
        added.UpdateDecision.Eligibility
            .Should().Be(UpdateEligibility.AlreadyUpToDate);

        // A new service instance over the same file simulates an app restart.
        var reloaded = await CreateSut(storePath)
            .LoadAsync(CancellationToken.None);

        reloaded.Should().ContainSingle()
            .Which.Configuration.Id.Should().Be(added.Configuration.Id);
    }

    [Fact]
    public async Task RefreshAsync_SeesLocalChangesWithoutNetwork()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.CommitFileAsync(repo, "a.txt", "A", "Commit A");
        await factory.PushAsync(repo);

        var sut = CreateSut(StorePath(factory));
        var added = await sut.AddAsync(repo, CancellationToken.None);
        added.Snapshot.IsDirty.Should().BeFalse();

        await File.WriteAllTextAsync(
            Path.Combine(repo, "uncommitted.txt"), "dirty");

        var refreshed = await sut.RefreshAsync(
            added.Configuration.Id, CancellationToken.None);

        refreshed.Snapshot.IsDirty.Should().BeTrue();
        refreshed.UpdateDecision.Eligibility
            .Should().Be(UpdateEligibility.Dirty);
    }

    [Fact]
    public async Task RemoveAsync_RemovesEntryButKeepsWorkingDirectory()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repo = await factory.CloneAsync(remote, "repo");
        await factory.CommitFileAsync(repo, "a.txt", "A", "Commit A");
        await factory.PushAsync(repo);

        var storePath = StorePath(factory);
        var sut = CreateSut(storePath);
        var added = await sut.AddAsync(repo, CancellationToken.None);

        await sut.RemoveAsync(added.Configuration.Id, CancellationToken.None);

        (await sut.LoadAsync(CancellationToken.None)).Should().BeEmpty();
        Directory.Exists(repo).Should().BeTrue();
        Directory.Exists(Path.Combine(repo, ".git")).Should().BeTrue();
    }

    [Fact]
    public async Task FetchAsync_SeesRemoteCommitsAfterFetch()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repoA = await factory.CloneAsync(remote, "repo-a");
        await factory.CommitFileAsync(repoA, "a.txt", "A", "Commit A");
        await factory.PushAsync(repoA);

        var repoB = await factory.CloneAsync(remote, "repo-b");

        var sut = CreateSut(StorePath(factory));
        var added = await sut.AddAsync(repoB, CancellationToken.None);
        added.Snapshot.DefaultBranchDivergence.Should().Be(new Divergence(0, 0));

        // Remote moves while repo-b sleeps: local divergence is stale (0, 0).
        await factory.CommitFileAsync(repoA, "b.txt", "B", "Commit B");
        await factory.PushAsync(repoA);

        var fetched = await sut.FetchAsync(
            added.Configuration.Id, CancellationToken.None);

        fetched.FetchError.Should().BeNull();
        fetched.InspectionError.Should().BeNull();
        fetched.LastSuccessfulFetch.Should().NotBeNull();
        fetched.Snapshot.DefaultBranchDivergence.Should().Be(new Divergence(0, 1));
        fetched.UpdateDecision.Eligibility
            .Should().Be(UpdateEligibility.CanFastForward);
    }

    [Fact]
    public async Task FetchAsync_UnknownId_ThrowsKeyNotFound()
    {
        using var factory = new GitTestRepositoryFactory();

        var sut = CreateSut(StorePath(factory));

        var act = () => sut.FetchAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task FetchAllAsync_ContinuesDespiteBrokenEntry()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var first = await factory.CloneAsync(remote, "first");
        await factory.CommitFileAsync(first, "a.txt", "A", "Commit A");
        await factory.PushAsync(first);
        var second = await factory.CloneAsync(remote, "second");

        var ghostPath = Path.Combine(
            factory.RootPath, "does-not-exist", "ghost");

        var storePath = StorePath(factory);
        var seed = new JsonRepositoryConfigurationStore(storePath);
        await seed.SaveAsync(
            [
                new RepositoryConfiguration
                {
                    Id = Guid.NewGuid(),
                    Name = "first",
                    Path = first
                },
                new RepositoryConfiguration
                {
                    Id = Guid.NewGuid(),
                    Name = "ghost",
                    Path = ghostPath
                },
                new RepositoryConfiguration
                {
                    Id = Guid.NewGuid(),
                    Name = "second",
                    Path = second
                }
            ],
            CancellationToken.None);

        var sut = CreateSut(storePath);

        var items = await sut.FetchAllAsync(CancellationToken.None);

        items.Should().HaveCount(3);

        items[0].FetchError.Should().BeNull();
        items[0].LastSuccessfulFetch.Should().NotBeNull();
        items[2].FetchError.Should().BeNull();
        items[2].LastSuccessfulFetch.Should().NotBeNull();

        var broken = items[1];
        broken.Configuration.Name.Should().Be("ghost");
        broken.FetchError.Should().NotBeNullOrWhiteSpace();
        broken.LastSuccessfulFetch.Should().BeNull();
        broken.Snapshot.DirectoryExists.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_BehindOnly_UpdatesAndReturnsFreshRow()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repoA = await factory.CloneAsync(remote, "repo-a");
        await factory.CommitFileAsync(repoA, "a.txt", "A", "Commit A");
        await factory.PushAsync(repoA);
        var repoB = await factory.CloneAsync(remote, "repo-b");

        var sut = CreateSut(StorePath(factory));
        var added = await sut.AddAsync(repoB, CancellationToken.None);

        await factory.CommitFileAsync(repoA, "b.txt", "B", "Commit B");
        await factory.PushAsync(repoA);

        var updated = await sut.UpdateAsync(
            added.Configuration.Id, CancellationToken.None);

        updated.UpdateResult.Should().NotBeNull();
        updated.UpdateResult!.Outcome.Should().Be(RepositoryUpdateOutcome.Updated);
        updated.InspectionError.Should().BeNull();
        updated.FetchError.Should().BeNull();
        updated.LastSuccessfulFetch.Should().NotBeNull();
        updated.Snapshot.DefaultBranchDivergence.Should().Be(new Divergence(0, 0));
        updated.UpdateDecision.Eligibility
            .Should().Be(UpdateEligibility.AlreadyUpToDate);
        File.Exists(Path.Combine(repoB, "b.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ThrowsKeyNotFound()
    {
        using var factory = new GitTestRepositoryFactory();

        var sut = CreateSut(StorePath(factory));

        var act = () => sut.UpdateAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateAllAsync_MixedOutcomes_CollectsAll()
    {
        using var factory = new GitTestRepositoryFactory();
        var remote = await factory.CreateBareRepositoryAsync();
        var repoA = await factory.CloneAsync(remote, "repo-a");
        await factory.CommitFileAsync(repoA, "a.txt", "A", "Commit A");
        await factory.PushAsync(repoA);

        // current: already at the tip → Skipped (already up to date).
        // behind: one commit behind the tip → Updated.
        // Cloned at different times so their starting states differ.
        var behind = await factory.CloneAsync(remote, "behind");

        await factory.CommitFileAsync(repoA, "b.txt", "B", "Commit B");
        await factory.PushAsync(repoA);

        var current = await factory.CloneAsync(remote, "current");

        var ghostPath = Path.Combine(
            factory.RootPath, "does-not-exist", "ghost");

        var storePath = StorePath(factory);
        var seed = new JsonRepositoryConfigurationStore(storePath);
        await seed.SaveAsync(
            [
                new RepositoryConfiguration
                {
                    Id = Guid.NewGuid(),
                    Name = "current",
                    Path = current
                },
                new RepositoryConfiguration
                {
                    Id = Guid.NewGuid(),
                    Name = "ghost",
                    Path = ghostPath
                },
                new RepositoryConfiguration
                {
                    Id = Guid.NewGuid(),
                    Name = "behind",
                    Path = behind
                }
            ],
            CancellationToken.None);

        var sut = CreateSut(storePath);

        var items = await sut.UpdateAllAsync(CancellationToken.None);

        items.Should().HaveCount(3);
        items.Select(i => i.Configuration.Name)
            .Should().Equal("current", "ghost", "behind");

        items[0].UpdateResult!.Outcome.Should().Be(RepositoryUpdateOutcome.Skipped);
        items[0].UpdateResult!.Decision?.Eligibility
            .Should().Be(UpdateEligibility.AlreadyUpToDate);
        items[0].LastSuccessfulFetch.Should().NotBeNull();

        items[1].UpdateResult!.Outcome.Should().Be(RepositoryUpdateOutcome.Failed);
        items[1].Snapshot.DirectoryExists.Should().BeFalse();
        items[1].LastSuccessfulFetch.Should().BeNull();

        items[2].UpdateResult!.Outcome.Should().Be(RepositoryUpdateOutcome.Updated);
        items[2].Snapshot.DefaultBranchDivergence.Should().Be(new Divergence(0, 0));
        items[2].LastSuccessfulFetch.Should().NotBeNull();
        File.Exists(Path.Combine(behind, "b.txt")).Should().BeTrue();
    }
}
