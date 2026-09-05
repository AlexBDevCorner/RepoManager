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

    private sealed class StubFetcher : IRepositoryFetcher
    {
        private readonly Func<RepositoryConfiguration, RepositoryOperationResult> _fetch;

        public int Calls { get; private set; }

        public StubFetcher(
            Func<RepositoryConfiguration, RepositoryOperationResult>? fetch = null)
        {
            _fetch = fetch ?? (c => new RepositoryOperationResult
            {
                Success = true,
                Operation = RepositoryOperationType.Fetch,
                Message = "Fetched 'origin' (pruned).",
                Duration = TimeSpan.Zero
            });
        }

        public Task<RepositoryOperationResult> FetchAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_fetch(repository));
        }
    }

    private sealed class StubUpdater : IRepositoryUpdater
    {
        private readonly Func<RepositoryConfiguration, RepositoryUpdateResult> _update;

        public int Calls { get; private set; }

        public StubUpdater(
            Func<RepositoryConfiguration, RepositoryUpdateResult>? update = null)
        {
            _update = update ?? (c => new RepositoryUpdateResult
            {
                RepositoryId = c.Id,
                Outcome = RepositoryUpdateOutcome.Failed,
                Message = "Unexpected update call."
            });
        }

        public Task<RepositoryUpdateResult> UpdateAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_update(repository));
        }
    }

    private static RepositoryDashboardService CreateSut(
        InMemoryStore store,
        StubInspector inspector,
        IRepositoryFetcher? fetcher = null,
        IRepositoryUpdater? updater = null) =>
        new(
            store,
            inspector,
            new UpdateEligibilityClassifier(),
            fetcher ?? new StubFetcher(),
            updater ?? new StubUpdater());

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

    [Fact]
    public async Task FetchAsync_SuccessfulFetch_ReinspectsAndRecordsTimestamp()
    {
        var configuration = Config("Store");
        var store = new InMemoryStore([configuration]);
        var inspector = new StubInspector(UpToDateSnapshot);
        var fetcher = new StubFetcher();
        var sut = CreateSut(store, inspector, fetcher);
        var before = DateTimeOffset.UtcNow;

        var item = await sut.FetchAsync(configuration.Id, CancellationToken.None);

        fetcher.Calls.Should().Be(1);
        // The item must come from a full re-inspection, never patched manually.
        inspector.Calls.Should().Be(1);
        item.Snapshot.Should().BeEquivalentTo(
            UpToDateSnapshot(configuration),
            o => o.Excluding(s => s.InspectedAt));
        item.FetchError.Should().BeNull();
        item.InspectionError.Should().BeNull();
        item.LastSuccessfulFetch.Should().NotBeNull();
        item.LastSuccessfulFetch!.Value.Should().BeOnOrAfter(before);
        item.LastSuccessfulFetch!.Value.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task FetchAsync_UnknownId_ThrowsKeyNotFound()
    {
        var inspector = new StubInspector(UpToDateSnapshot);
        var fetcher = new StubFetcher();
        var sut = CreateSut(new InMemoryStore([Config()]), inspector, fetcher);

        var act = () => sut.FetchAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        fetcher.Calls.Should().Be(0);
        inspector.Calls.Should().Be(0);
    }

    [Fact]
    public async Task FetchAsync_FailedFetch_KeepsLocalStateWithFetchError()
    {
        var configuration = Config("Store");
        var store = new InMemoryStore([configuration]);
        var inspector = new StubInspector(UpToDateSnapshot);
        var fetcher = new StubFetcher(c => new RepositoryOperationResult
        {
            Success = false,
            Operation = RepositoryOperationType.Fetch,
            Message = "git fetch origin --prune failed with exit code 128: boom",
            ExitCode = 128,
            Duration = TimeSpan.Zero
        });
        var sut = CreateSut(store, inspector, fetcher);

        var item = await sut.FetchAsync(configuration.Id, CancellationToken.None);

        fetcher.Calls.Should().Be(1);
        inspector.Calls.Should().Be(1);
        item.FetchError.Should().Contain("boom");
        item.InspectionError.Should().BeNull();
        item.LastSuccessfulFetch.Should().BeNull();
        // Local state is still classified normally — only the fetch failed.
        item.UpdateDecision.Eligibility
            .Should().Be(UpdateEligibility.AlreadyUpToDate);
        item.Snapshot.CurrentBranch.Should().Be("main");
    }

    [Fact]
    public async Task FetchAsync_ThrowingFetcher_KeepsLocalStateWithFetchError()
    {
        var configuration = Config("Store");
        var store = new InMemoryStore([configuration]);
        var inspector = new StubInspector(UpToDateSnapshot);
        var fetcher = new StubFetcher(
            _ => throw new InvalidOperationException("network down"));
        var sut = CreateSut(store, inspector, fetcher);

        var item = await sut.FetchAsync(configuration.Id, CancellationToken.None);

        item.FetchError.Should().Contain("network down");
        item.InspectionError.Should().BeNull();
        item.LastSuccessfulFetch.Should().BeNull();
        item.Snapshot.CurrentBranch.Should().Be("main");
    }

    [Fact]
    public async Task FetchAllAsync_CollectsAllResultsDespiteFailure()
    {
        var first = Config("First", """C:\Source\Repos\First""");
        var broken = Config("Broken", """C:\Source\Repos\Broken""");
        var third = Config("Third", """C:\Source\Repos\Third""");
        var store = new InMemoryStore([first, broken, third]);
        var inspector = new StubInspector(UpToDateSnapshot);
        var fetcher = new StubFetcher(c => c.Name == "Broken"
            ? new RepositoryOperationResult
            {
                Success = false,
                Operation = RepositoryOperationType.Fetch,
                Message = "fetch boom",
                Duration = TimeSpan.Zero
            }
            : new RepositoryOperationResult
            {
                Success = true,
                Operation = RepositoryOperationType.Fetch,
                Message = "Fetched 'origin' (pruned).",
                Duration = TimeSpan.Zero
            });
        var sut = CreateSut(store, inspector, fetcher);

        var items = await sut.FetchAllAsync(CancellationToken.None);

        items.Should().HaveCount(3);
        fetcher.Calls.Should().Be(3);
        inspector.Calls.Should().Be(3);

        items.Select(i => i.Configuration.Name)
            .Should().Equal("First", "Broken", "Third");

        items[0].FetchError.Should().BeNull();
        items[0].LastSuccessfulFetch.Should().NotBeNull();
        items[2].FetchError.Should().BeNull();
        items[2].LastSuccessfulFetch.Should().NotBeNull();

        var failed = items[1];
        failed.FetchError.Should().Contain("fetch boom");
        failed.InspectionError.Should().BeNull();
        failed.LastSuccessfulFetch.Should().BeNull();
        failed.Snapshot.CurrentBranch.Should().Be("main");
    }

    [Fact]
    public async Task FetchAllAsync_EmptyStore_ReturnsEmpty()
    {
        var fetcher = new StubFetcher();
        var sut = CreateSut(
            new InMemoryStore(),
            new StubInspector(UpToDateSnapshot),
            fetcher);

        var items = await sut.FetchAllAsync(CancellationToken.None);

        items.Should().BeEmpty();
        fetcher.Calls.Should().Be(0);
    }

    [Fact]
    public async Task FetchAsync_SuccessThenRefresh_PreservesLastSuccessfulFetch()
    {
        var configuration = Config("Store");
        var store = new InMemoryStore([configuration]);
        var sut = CreateSut(
            store,
            new StubInspector(UpToDateSnapshot),
            new StubFetcher());

        var fetched = await sut.FetchAsync(configuration.Id, CancellationToken.None);
        var refreshed = await sut.RefreshAsync(configuration.Id, CancellationToken.None);

        fetched.LastSuccessfulFetch.Should().NotBeNull();
        refreshed.LastSuccessfulFetch.Should().Be(fetched.LastSuccessfulFetch);
        refreshed.FetchError.Should().BeNull();
    }

    /// <summary>
    /// Tracks concurrent entries across one or more stubs. Sharing a single
    /// probe between a fetcher and an updater proves they mutually exclude
    /// on the same repository (the PR #8 review requirement).
    /// </summary>
    private sealed class SharedProbe
    {
        private int _current;

        public int Max;

        public void Enter()
        {
            var current = Interlocked.Increment(ref _current);

            int previous;
            do
            {
                previous = Max;
            }
            while (previous < current &&
                Interlocked.CompareExchange(ref Max, current, previous) != previous);
        }

        public void Exit() => Interlocked.Decrement(ref _current);
    }

    private sealed class TrackingFetcher : IRepositoryFetcher
    {
        private readonly TimeSpan _delay;
        private readonly SharedProbe _probe;

        public int Calls;

        public int MaxConcurrent => _probe.Max;

        public TrackingFetcher(TimeSpan delay, SharedProbe? probe = null)
        {
            _delay = delay;
            _probe = probe ?? new SharedProbe();
        }

        public async Task<RepositoryOperationResult> FetchAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            _probe.Enter();

            try
            {
                await Task.Delay(_delay, cancellationToken);
                return new RepositoryOperationResult
                {
                    Success = true,
                    Operation = RepositoryOperationType.Fetch,
                    Message = "Fetched 'origin' (pruned).",
                    Duration = _delay
                };
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    private sealed class TrackingUpdater : IRepositoryUpdater
    {
        private readonly TimeSpan _delay;
        private readonly SharedProbe _probe;
        private readonly Func<RepositoryConfiguration, RepositorySnapshot> _snapshot;

        public int Calls;

        public int MaxConcurrent => _probe.Max;

        public TrackingUpdater(
            TimeSpan delay,
            SharedProbe? probe = null,
            Func<RepositoryConfiguration, RepositorySnapshot>? snapshot = null)
        {
            _delay = delay;
            _probe = probe ?? new SharedProbe();
            _snapshot = snapshot ?? UpToDateSnapshot;
        }

        public async Task<RepositoryUpdateResult> UpdateAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            _probe.Enter();

            try
            {
                await Task.Delay(_delay, cancellationToken);
                return new RepositoryUpdateResult
                {
                    RepositoryId = repository.Id,
                    Outcome = RepositoryUpdateOutcome.Updated,
                    Message = "Fast-forwarded 'main' by 1 commit(s) from 'origin/main'.",
                    Decision = new UpdateDecision(
                        UpdateEligibility.CanFastForward,
                        "Current branch can fast-forward by 1 commit(s)."),
                    FetchResult = new RepositoryOperationResult
                    {
                        Success = true,
                        Operation = RepositoryOperationType.Fetch,
                        Message = "Fetched 'origin' (pruned).",
                        Duration = TimeSpan.Zero
                    },
                    FinalSnapshot = _snapshot(repository)
                };
            }
            finally
            {
                _probe.Exit();
            }
        }
    }

    [Fact]
    public async Task FetchAllAsync_BoundsConcurrencyToFour()
    {
        var configurations = Enumerable.Range(0, 10)
            .Select(i => Config($"Repo{i}", $"""C:\Source\Repos\Repo{i}"""))
            .ToList();
        var store = new InMemoryStore(configurations);
        var fetcher = new TrackingFetcher(TimeSpan.FromMilliseconds(100));
        var sut = CreateSut(
            store,
            new StubInspector(UpToDateSnapshot),
            fetcher);

        var items = await sut.FetchAllAsync(CancellationToken.None);

        items.Should().HaveCount(10);
        items.Should().OnlyContain(i => i.FetchError == null);
        items.Should().OnlyContain(i => i.LastSuccessfulFetch != null);
        fetcher.MaxConcurrent.Should().BeLessThanOrEqualTo(4);
    }

    [Fact]
    public async Task FetchAsync_SameRepository_SerializesConcurrentFetches()
    {
        var configuration = Config("Store");
        var store = new InMemoryStore([configuration]);
        var fetcher = new TrackingFetcher(TimeSpan.FromMilliseconds(200));
        var sut = CreateSut(
            store,
            new StubInspector(UpToDateSnapshot),
            fetcher);

        var first = sut.FetchAsync(configuration.Id, CancellationToken.None);
        var second = sut.FetchAsync(configuration.Id, CancellationToken.None);
        await Task.WhenAll(first, second);

        fetcher.Calls.Should().Be(2);
        fetcher.MaxConcurrent.Should().Be(1);
    }

    [Fact]
    public async Task FetchAllAsync_DifferentRepositories_RunInParallel()
    {
        var configurations = Enumerable.Range(0, 4)
            .Select(i => Config($"Repo{i}", $"""C:\Source\Repos\Repo{i}"""))
            .ToList();
        var store = new InMemoryStore(configurations);
        var fetcher = new TrackingFetcher(TimeSpan.FromMilliseconds(200));
        var sut = CreateSut(
            store,
            new StubInspector(UpToDateSnapshot),
            fetcher);

        var items = await sut.FetchAllAsync(CancellationToken.None);

        items.Should().HaveCount(4);
        fetcher.MaxConcurrent.Should().BeInRange(2, 4);
    }

    private static RepositoryOperationResult SuccessfulFetch() =>
        new()
        {
            Success = true,
            Operation = RepositoryOperationType.Fetch,
            Message = "Fetched 'origin' (pruned).",
            Duration = TimeSpan.Zero
        };

    private static RepositoryOperationResult FailedFetch() =>
        new()
        {
            Success = false,
            Operation = RepositoryOperationType.Fetch,
            Message = "git fetch origin --prune failed with exit code 128: boom",
            ExitCode = 128,
            Duration = TimeSpan.Zero
        };

    [Fact]
    public async Task UpdateAsync_SuccessfulUpdate_UsesFinalSnapshotWithoutReinspecting()
    {
        var configuration = Config("Store");
        var store = new InMemoryStore([configuration]);
        var inspector = new StubInspector(UpToDateSnapshot);
        var updater = new StubUpdater(c => new RepositoryUpdateResult
        {
            RepositoryId = c.Id,
            Outcome = RepositoryUpdateOutcome.Updated,
            Message = "Fast-forwarded 'main' by 1 commit(s) from 'origin/main'.",
            Decision = new UpdateDecision(
                UpdateEligibility.CanFastForward,
                "Current branch can fast-forward by 1 commit(s)."),
            FetchResult = SuccessfulFetch(),
            FinalSnapshot = UpToDateSnapshot(c)
        });
        var sut = CreateSut(store, inspector, updater: updater);

        var item = await sut.UpdateAsync(configuration.Id, CancellationToken.None);

        updater.Calls.Should().Be(1);
        // The updater's final re-inspection is authoritative: no third inspect.
        inspector.Calls.Should().Be(0);
        item.Snapshot.Should().BeEquivalentTo(
            UpToDateSnapshot(configuration),
            o => o.Excluding(s => s.InspectedAt));
        item.UpdateResult.Should().NotBeNull();
        item.UpdateResult!.Outcome.Should().Be(RepositoryUpdateOutcome.Updated);
        item.UpdateResult.Message.Should().Contain("Fast-forward");
        item.InspectionError.Should().BeNull();
        item.FetchError.Should().BeNull();
        // An update starts with a successful fetch, so it stamps the fetch time.
        item.LastSuccessfulFetch.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ThrowsKeyNotFound()
    {
        var updater = new StubUpdater();
        var sut = CreateSut(
            new InMemoryStore([Config()]),
            new StubInspector(UpToDateSnapshot),
            updater: updater);

        var act = () => sut.UpdateAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        updater.Calls.Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_Skipped_PreservesReasonAndFetchTimestamp()
    {
        var configuration = Config("Store");
        var store = new InMemoryStore([configuration]);
        var inspector = new StubInspector(UpToDateSnapshot);
        var updater = new StubUpdater(c => new RepositoryUpdateResult
        {
            RepositoryId = c.Id,
            Outcome = RepositoryUpdateOutcome.Skipped,
            Message = "The working tree contains uncommitted changes.",
            Decision = new UpdateDecision(
                UpdateEligibility.Dirty,
                "The working tree contains uncommitted changes."),
            FetchResult = SuccessfulFetch(),
            FinalSnapshot = UpToDateSnapshot(c)
        });
        var sut = CreateSut(store, inspector, updater: updater);

        var item = await sut.UpdateAsync(configuration.Id, CancellationToken.None);

        inspector.Calls.Should().Be(0);
        item.UpdateResult!.Outcome.Should().Be(RepositoryUpdateOutcome.Skipped);
        item.UpdateResult.Message.Should().Contain("uncommitted changes");
        item.InspectionError.Should().BeNull();
        item.LastSuccessfulFetch.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_FailedWithoutSnapshot_FallsBackToInspection()
    {
        var configuration = Config("Store");
        var store = new InMemoryStore([configuration]);
        var inspector = new StubInspector(UpToDateSnapshot);
        var updater = new StubUpdater(c => new RepositoryUpdateResult
        {
            RepositoryId = c.Id,
            Outcome = RepositoryUpdateOutcome.Failed,
            Message = "git fetch origin --prune failed with exit code 128: boom",
            FetchResult = FailedFetch(),
            FinalSnapshot = null
        });
        var sut = CreateSut(store, inspector, updater: updater);

        var item = await sut.UpdateAsync(configuration.Id, CancellationToken.None);

        inspector.Calls.Should().Be(1);
        item.Snapshot.CurrentBranch.Should().Be("main");
        item.UpdateResult!.Outcome.Should().Be(RepositoryUpdateOutcome.Failed);
        item.UpdateResult.Message.Should().Contain("boom");
        item.InspectionError.Should().BeNull();
        item.LastSuccessfulFetch.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ThrowingUpdater_BuildsFailedRowWithOutcome()
    {
        var configuration = Config("Store");
        var store = new InMemoryStore([configuration]);
        var inspector = new StubInspector(UpToDateSnapshot);
        var updater = new StubUpdater(
            _ => throw new InvalidOperationException("boom"));
        var sut = CreateSut(store, inspector, updater: updater);

        var item = await sut.UpdateAsync(configuration.Id, CancellationToken.None);

        item.UpdateResult.Should().NotBeNull();
        item.UpdateResult!.Outcome.Should().Be(RepositoryUpdateOutcome.Failed);
        item.UpdateResult.Message.Should().Contain("boom");
        item.Snapshot.CurrentBranch.Should().Be("main");
        item.LastSuccessfulFetch.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAllAsync_CollectsMixedOutcomesInOrder()
    {
        var first = Config("First", """C:\Source\Repos\First""");
        var broken = Config("Broken", """C:\Source\Repos\Broken""");
        var third = Config("Third", """C:\Source\Repos\Third""");
        var store = new InMemoryStore([first, broken, third]);
        var inspector = new StubInspector(UpToDateSnapshot);
        var updater = new StubUpdater(c => c.Name switch
        {
            "Broken" => new RepositoryUpdateResult
            {
                RepositoryId = c.Id,
                Outcome = RepositoryUpdateOutcome.Failed,
                Message = "update boom",
                FetchResult = FailedFetch(),
                FinalSnapshot = null
            },
            "Third" => new RepositoryUpdateResult
            {
                RepositoryId = c.Id,
                Outcome = RepositoryUpdateOutcome.Skipped,
                Message = "Current branch is already up to date.",
                Decision = new UpdateDecision(
                    UpdateEligibility.AlreadyUpToDate,
                    "Current branch is already up to date."),
                FetchResult = SuccessfulFetch(),
                FinalSnapshot = UpToDateSnapshot(c)
            },
            _ => new RepositoryUpdateResult
            {
                RepositoryId = c.Id,
                Outcome = RepositoryUpdateOutcome.Updated,
                Message = "Fast-forwarded 'main'.",
                Decision = new UpdateDecision(
                    UpdateEligibility.CanFastForward,
                    "Current branch can fast-forward by 1 commit(s)."),
                FetchResult = SuccessfulFetch(),
                FinalSnapshot = UpToDateSnapshot(c)
            }
        });
        var sut = CreateSut(store, inspector, updater: updater);

        var items = await sut.UpdateAllAsync(CancellationToken.None);

        items.Should().HaveCount(3);
        updater.Calls.Should().Be(3);
        // Only the failed-without-snapshot entry needed a fallback inspect.
        inspector.Calls.Should().Be(1);

        items.Select(i => i.Configuration.Name)
            .Should().Equal("First", "Broken", "Third");

        items[0].UpdateResult!.Outcome.Should().Be(RepositoryUpdateOutcome.Updated);
        items[0].LastSuccessfulFetch.Should().NotBeNull();

        var failed = items[1];
        failed.UpdateResult!.Outcome.Should().Be(RepositoryUpdateOutcome.Failed);
        failed.UpdateResult.Message.Should().Contain("update boom");
        failed.InspectionError.Should().BeNull();
        failed.Snapshot.CurrentBranch.Should().Be("main");
        failed.LastSuccessfulFetch.Should().BeNull();

        items[2].UpdateResult!.Outcome.Should().Be(RepositoryUpdateOutcome.Skipped);
        items[2].LastSuccessfulFetch.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateAllAsync_EmptyStore_ReturnsEmpty()
    {
        var updater = new StubUpdater();
        var sut = CreateSut(
            new InMemoryStore(),
            new StubInspector(UpToDateSnapshot),
            updater: updater);

        var items = await sut.UpdateAllAsync(CancellationToken.None);

        items.Should().BeEmpty();
        updater.Calls.Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_SuccessThenRefresh_PreservesFetchTimestampButNotOutcome()
    {
        var configuration = Config("Store");
        var store = new InMemoryStore([configuration]);
        var sut = CreateSut(
            store,
            new StubInspector(UpToDateSnapshot),
            updater: new StubUpdater(c => new RepositoryUpdateResult
            {
                RepositoryId = c.Id,
                Outcome = RepositoryUpdateOutcome.Updated,
                Message = "Fast-forwarded 'main'.",
                FetchResult = SuccessfulFetch(),
                FinalSnapshot = UpToDateSnapshot(c)
            }));

        var updated = await sut.UpdateAsync(configuration.Id, CancellationToken.None);
        var refreshed = await sut.RefreshAsync(configuration.Id, CancellationToken.None);

        updated.LastSuccessfulFetch.Should().NotBeNull();
        refreshed.LastSuccessfulFetch.Should().Be(updated.LastSuccessfulFetch);
        // A local refresh is not an update attempt: no stale outcome survives.
        refreshed.UpdateResult.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAllAsync_BoundsConcurrencyToFour()
    {
        var configurations = Enumerable.Range(0, 10)
            .Select(i => Config($"Repo{i}", $"""C:\Source\Repos\Repo{i}"""))
            .ToList();
        var store = new InMemoryStore(configurations);
        var updater = new TrackingUpdater(TimeSpan.FromMilliseconds(100));
        var sut = CreateSut(
            store,
            new StubInspector(UpToDateSnapshot),
            updater: updater);

        var items = await sut.UpdateAllAsync(CancellationToken.None);

        items.Should().HaveCount(10);
        items.Should().OnlyContain(
            i => i.UpdateResult != null &&
                i.UpdateResult.Outcome == RepositoryUpdateOutcome.Updated);
        updater.MaxConcurrent.Should().BeLessThanOrEqualTo(4);
    }

    [Fact]
    public async Task UpdateAsync_SameRepository_SerializesConcurrentUpdates()
    {
        var configuration = Config("Store");
        var store = new InMemoryStore([configuration]);
        var updater = new TrackingUpdater(TimeSpan.FromMilliseconds(200));
        var sut = CreateSut(
            store,
            new StubInspector(UpToDateSnapshot),
            updater: updater);

        var first = sut.UpdateAsync(configuration.Id, CancellationToken.None);
        var second = sut.UpdateAsync(configuration.Id, CancellationToken.None);
        await Task.WhenAll(first, second);

        updater.Calls.Should().Be(2);
        updater.MaxConcurrent.Should().Be(1);
    }

    [Fact]
    public async Task FetchAndUpdate_SameRepository_SerializeAcrossOperations()
    {
        // The PR #8 review requirement: fetch and update share one lock
        // registry and one semaphore, so they can never overlap on the
        // same repository even though they are different operations.
        var configuration = Config("Store");
        var store = new InMemoryStore([configuration]);
        var probe = new SharedProbe();
        var fetcher = new TrackingFetcher(TimeSpan.FromMilliseconds(200), probe);
        var updater = new TrackingUpdater(TimeSpan.FromMilliseconds(200), probe);
        var sut = CreateSut(
            store,
            new StubInspector(UpToDateSnapshot),
            fetcher,
            updater);

        var fetch = sut.FetchAsync(configuration.Id, CancellationToken.None);
        var update = sut.UpdateAsync(configuration.Id, CancellationToken.None);
        await Task.WhenAll(fetch, update);

        fetcher.Calls.Should().Be(1);
        updater.Calls.Should().Be(1);
        probe.Max.Should().Be(1);
    }
}
