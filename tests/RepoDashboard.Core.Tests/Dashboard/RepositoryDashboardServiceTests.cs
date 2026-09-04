using FluentAssertions;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Repositories;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.Core.Tests.Dashboard;

public sealed class RepositoryDashboardServiceTests : IDisposable
{
    private readonly List<string> _directoriesToDelete = [];

    private static RepositoryConfiguration Config(
        string name = "Store",
        string? path = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = path ?? """C:\Source\Repos\Store"""
        };

    private static RepositorySnapshot UpToDateSnapshot(
        RepositoryConfiguration configuration) =>
        new()
        {
            RepositoryId = configuration.Id,
            Path = configuration.Path,
            DirectoryExists = true,
            IsGitRepository = true,
            CurrentBranch = "main",
            IsDirty = false,
            UpstreamRef = "origin/main",
            UpstreamRemote = "origin",
            UpstreamBranch = "main",
            DefaultRemoteBranch = "main",
            UpstreamDivergence = new Divergence(0, 0),
            DefaultBranchDivergence = new Divergence(0, 0),
            InspectedAt = DateTimeOffset.UtcNow
        };

    private sealed class InMemoryStore : IRepositoryConfigurationStore
    {
        private List<RepositoryConfiguration> _entries;

        public int SaveCalls { get; private set; }

        public InMemoryStore(IEnumerable<RepositoryConfiguration>? seed = null)
        {
            _entries = seed?.ToList() ?? [];
        }

        public Task<IReadOnlyList<RepositoryConfiguration>> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RepositoryConfiguration>>(
                _entries.ToList());

        public Task SaveAsync(
            IReadOnlyCollection<RepositoryConfiguration> repositories,
            CancellationToken cancellationToken)
        {
            SaveCalls++;
            _entries = repositories.ToList();
            return Task.CompletedTask;
        }
    }

    private sealed class StubInspector : IRepositoryInspector
    {
        private readonly Func<RepositoryConfiguration, RepositorySnapshot> _inspect;

        public int Calls { get; private set; }

        public StubInspector(
            Func<RepositoryConfiguration, RepositorySnapshot> inspect)
        {
            _inspect = inspect;
        }

        public Task<RepositorySnapshot> InspectAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_inspect(repository));
        }
    }

    private static RepositoryDashboardService CreateSut(
        InMemoryStore store,
        StubInspector inspector) =>
        new(store, inspector, new UpdateEligibilityClassifier());

    private string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "RepoDashboard.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _directoriesToDelete.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var directory in _directoriesToDelete)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                // Best effort: never fail the test run on cleanup.
            }
        }
    }

    [Fact]
    public async Task LoadAsync_ReturnsOneItemPerConfiguration()
    {
        var first = Config("Store");
        var second = Config("Legacy", """C:\Source\Repos\Legacy""");
        var store = new InMemoryStore([first, second]);
        var inspector = new StubInspector(UpToDateSnapshot);
        var sut = CreateSut(store, inspector);

        var items = await sut.LoadAsync(CancellationToken.None);

        items.Should().HaveCount(2);
        items.Select(i => i.Configuration.Name)
            .Should().BeEquivalentTo("Store", "Legacy");
        items.Should().OnlyContain(
            i => i.UpdateDecision.Eligibility == UpdateEligibility.AlreadyUpToDate);
        items.Should().OnlyContain(i => i.LastSuccessfulFetch == null);
        inspector.Calls.Should().Be(2);
    }

    [Fact]
    public async Task LoadAsync_EmptyStore_ReturnsEmpty()
    {
        var sut = CreateSut(
            new InMemoryStore(),
            new StubInspector(UpToDateSnapshot));

        var items = await sut.LoadAsync(CancellationToken.None);

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshAsync_ReturnsSingleItem()
    {
        var first = Config("Store");
        var second = Config("Legacy", """C:\Source\Repos\Legacy""");
        var store = new InMemoryStore([first, second]);
        var inspector = new StubInspector(UpToDateSnapshot);
        var sut = CreateSut(store, inspector);

        var item = await sut.RefreshAsync(first.Id, CancellationToken.None);

        item.Configuration.Id.Should().Be(first.Id);
        item.Snapshot.RepositoryId.Should().Be(first.Id);
        item.UpdateDecision.Eligibility
            .Should().Be(UpdateEligibility.AlreadyUpToDate);
        inspector.Calls.Should().Be(1);
    }

    [Fact]
    public async Task RefreshAsync_UnknownId_ThrowsKeyNotFound()
    {
        var sut = CreateSut(
            new InMemoryStore([Config()]),
            new StubInspector(UpToDateSnapshot));

        var act = () => sut.RefreshAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task RefreshAllAsync_ReinspectsEveryRepository()
    {
        var first = Config("Store");
        var second = Config("Legacy", """C:\Source\Repos\Legacy""");
        var store = new InMemoryStore([first, second]);
        var inspector = new StubInspector(UpToDateSnapshot);
        var sut = CreateSut(store, inspector);

        await sut.LoadAsync(CancellationToken.None);
        var items = await sut.RefreshAllAsync(CancellationToken.None);

        items.Should().HaveCount(2);
        inspector.Calls.Should().Be(4);
    }

    [Fact]
    public async Task RefreshAllAsync_SingleFailure_DoesNotAbortOthers()
    {
        var first = Config("First", """C:\Source\Repos\First""");
        var broken = Config("Broken", """C:\Source\Repos\Broken""");
        var third = Config("Third", """C:\Source\Repos\Third""");
        var store = new InMemoryStore([first, broken, third]);
        var inspector = new StubInspector(c =>
            c.Name == "Broken"
                ? throw new InvalidOperationException("git status unexpectedly failed")
                : UpToDateSnapshot(c));
        var sut = CreateSut(store, inspector);

        var items = await sut.RefreshAllAsync(CancellationToken.None);

        items.Should().HaveCount(3);
        inspector.Calls.Should().Be(3);

        var failed = items[1];
        failed.Configuration.Name.Should().Be("Broken");
        failed.InspectionError.Should().Contain("git status unexpectedly failed");
        failed.UpdateDecision.Eligibility.Should().Be(UpdateEligibility.Unknown);
        failed.UpdateDecision.Explanation.Should().Be(failed.InspectionError);
        failed.Snapshot.RepositoryId.Should().Be(broken.Id);

        items[0].InspectionError.Should().BeNull();
        items[2].InspectionError.Should().BeNull();
        items[2].UpdateDecision.Eligibility
            .Should().Be(UpdateEligibility.AlreadyUpToDate);
    }

    [Fact]
    public async Task AddAsync_ValidGitDirectory_PersistsAndReturnsItem()
    {
        var directory = CreateTempDirectory();
        var store = new InMemoryStore();
        var inspector = new StubInspector(c => UpToDateSnapshot(c) with
        {
            Path = c.Path
        });
        var sut = CreateSut(store, inspector);

        var item = await sut.AddAsync(directory, CancellationToken.None);

        item.Configuration.Path
            .Should().Be(Path.GetFullPath(directory));
        item.Configuration.Name
            .Should().Be(new DirectoryInfo(directory).Name);
        item.Configuration.PreferredRemote.Should().Be("origin");
        item.Configuration.Enabled.Should().BeTrue();
        item.UpdateDecision.Eligibility
            .Should().Be(UpdateEligibility.AlreadyUpToDate);

        var reloaded = await store.LoadAsync(CancellationToken.None);
        reloaded.Should().ContainSingle()
            .Which.Id.Should().Be(item.Configuration.Id);
    }

    [Fact]
    public async Task AddAsync_MissingDirectory_ThrowsAndPersistsNothing()
    {
        var missing = Path.Combine(
            Path.GetTempPath(),
            "RepoDashboard.Tests",
            Guid.NewGuid().ToString("N"),
            "does-not-exist");
        var store = new InMemoryStore([Config()]);
        var sut = CreateSut(store, new StubInspector(UpToDateSnapshot));

        var act = () => sut.AddAsync(missing, CancellationToken.None);

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
        (await store.LoadAsync(CancellationToken.None))
            .Should().HaveCount(1);
        store.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task AddAsync_NotAGitRepository_ThrowsAndPersistsNothing()
    {
        var directory = CreateTempDirectory();
        var store = new InMemoryStore();
        var inspector = new StubInspector(c => UpToDateSnapshot(c) with
        {
            IsGitRepository = false
        });
        var sut = CreateSut(store, inspector);

        var act = () => sut.AddAsync(directory, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not a Git repository*");
        (await store.LoadAsync(CancellationToken.None)).Should().BeEmpty();
        store.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task AddAsync_DuplicatePathCaseInsensitive_ThrowsAndPersistsNothing()
    {
        var directory = CreateTempDirectory();
        var existing = Config(
            "Store",
            directory.ToLowerInvariant() + Path.DirectorySeparatorChar);
        var store = new InMemoryStore([existing]);
        var sut = CreateSut(store, new StubInspector(UpToDateSnapshot));

        var act = () => sut.AddAsync(directory, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already on the dashboard*");
        (await store.LoadAsync(CancellationToken.None))
            .Should().ContainSingle();
        store.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task RemoveAsync_RemovesEntryButKeepsFilesOnDisk()
    {
        var firstPath = CreateTempDirectory();
        var secondPath = CreateTempDirectory();
        var first = Config("First", firstPath);
        var second = Config("Second", secondPath);
        var store = new InMemoryStore([first, second]);
        var sut = CreateSut(store, new StubInspector(UpToDateSnapshot));

        await sut.RemoveAsync(first.Id, CancellationToken.None);

        (await store.LoadAsync(CancellationToken.None))
            .Should().ContainSingle()
            .Which.Id.Should().Be(second.Id);
        Directory.Exists(firstPath).Should().BeTrue(
            "removing from the dashboard must never delete files on disk");
        Directory.Exists(secondPath).Should().BeTrue();
    }

    [Fact]
    public async Task RemoveAsync_UnknownId_ThrowsKeyNotFound()
    {
        var store = new InMemoryStore([Config()]);
        var sut = CreateSut(store, new StubInspector(UpToDateSnapshot));

        var act = () => sut.RemoveAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        store.SaveCalls.Should().Be(0);
    }
}
