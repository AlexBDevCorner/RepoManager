using FluentAssertions;
using RepoDashboard.Core.Dashboard;
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
            new UpdateEligibilityClassifier());

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
}
