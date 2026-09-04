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
    private static RepositoryDashboardService CreateSut(string storePath) =>
        new(
            new JsonRepositoryConfigurationStore(storePath),
            new RepositoryInspector(
                new GitCommandRunner(),
                new DivergenceCalculator(new GitCommandRunner())),
            new UpdateEligibilityClassifier(),
            new RepositoryFetcher(new GitCommandRunner()));

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
}
