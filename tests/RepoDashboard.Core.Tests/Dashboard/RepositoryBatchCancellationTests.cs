using System.Collections.Concurrent;
using FluentAssertions;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Repositories;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.Core.Tests.Dashboard;

/// <summary>
/// Review fix: cancelling Fetch All / Update All must preserve completed
/// results instead of discarding them via Task.WhenAll. Six repositories
/// exceed the concurrency bound of 4, so two stay queued: after Cancel,
/// the queued ones must never start while the fast ones remain completed.
/// </summary>
public sealed class RepositoryBatchCancellationTests
{
    private static RepositoryConfiguration Config(string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = $"""C:\Source\Repos\{name}"""
        };

    private static RepositorySnapshot Snapshot(RepositoryConfiguration c) =>
        new()
        {
            RepositoryId = c.Id,
            Path = c.Path,
            DirectoryExists = true,
            IsGitRepository = true,
            CurrentBranch = "main",
            UpstreamRef = "origin/main",
            UpstreamRemote = "origin",
            UpstreamBranch = "main",
            UpstreamDivergence = new Divergence(0, 0),
            InspectedAt = DateTimeOffset.UtcNow
        };

    private sealed class InMemoryStore(
        IReadOnlyList<RepositoryConfiguration> seed) : IRepositoryConfigurationStore
    {
        public Task<IReadOnlyList<RepositoryConfiguration>> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(seed);

        public Task SaveAsync(
            IReadOnlyCollection<RepositoryConfiguration> repositories,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FixedInspector : IRepositoryInspector
    {
        public Task<RepositorySnapshot> InspectAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot(repository));
    }

    /// <summary>
    /// Fast repos complete immediately; slow repos block until cancelled.
    /// Started tracks every fetcher entry so the test can prove queued
    /// repositories never started.
    /// </summary>
    private sealed class GateFetcher : IRepositoryFetcher
    {
        private readonly TaskCompletionSource _gate = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentBag<string> Started { get; } = [];

        public Task<RepositoryOperationResult> FetchAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken)
        {
            Started.Add(repository.Name);

            if (repository.Name.StartsWith("fast-", StringComparison.Ordinal))
            {
                return Task.FromResult(new RepositoryOperationResult
                {
                    Success = true,
                    Operation = RepositoryOperationType.Fetch,
                    Message = "Fetched 'origin' (pruned).",
                    Duration = TimeSpan.Zero
                });
            }

            return FetchBlockedAsync(cancellationToken);
        }

        private async Task<RepositoryOperationResult> FetchBlockedAsync(
            CancellationToken cancellationToken)
        {
            await _gate.Task.WaitAsync(cancellationToken);
            throw new InvalidOperationException("Gate was released unexpectedly.");
        }
    }

    private sealed class GateUpdater : IRepositoryUpdater
    {
        private readonly TaskCompletionSource _gate = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentBag<string> Started { get; } = [];

        public async Task<RepositoryUpdateResult> UpdateAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken)
        {
            Started.Add(repository.Name);

            if (!repository.Name.StartsWith("fast-", StringComparison.Ordinal))
            {
                await _gate.Task.WaitAsync(cancellationToken);
                throw new InvalidOperationException("Gate was released unexpectedly.");
            }

            var snapshot = Snapshot(repository);

            return new RepositoryUpdateResult
            {
                RepositoryId = repository.Id,
                Outcome = RepositoryUpdateOutcome.Skipped,
                Message = "Already up to date.",
                Decision = new UpdateDecision(
                    UpdateEligibility.AlreadyUpToDate, "up to date"),
                FetchResult = new RepositoryOperationResult
                {
                    Success = true,
                    Operation = RepositoryOperationType.Fetch,
                    Message = "Fetched 'origin' (pruned).",
                    Duration = TimeSpan.Zero
                },
                FinalSnapshot = snapshot
            };
        }
    }

    private sealed class UnusedFetcher : IRepositoryFetcher
    {
        public Task<RepositoryOperationResult> FetchAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedUpdater : IRepositoryUpdater
    {
        public Task<RepositoryUpdateResult> UpdateAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static async Task WaitForAsync(
        Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }

            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task FetchAllAsync_Cancel_PreservesCompleted_QueuedNeverStart()
    {
        // Two fast repos complete synchronously and release their slots;
        // the first four slow repos then occupy all 4 semaphore slots and
        // block; the fifth slow repo stays queued behind the semaphore and
        // must never reach the fetcher.
        var names = new[] { "fast-0", "fast-1", "slow-0", "slow-1", "slow-2", "slow-3", "slow-4" };
        var configs = names.Select(Config).ToList();
        var fetcher = new GateFetcher();
        var sut = new RepositoryDashboardService(
            new InMemoryStore(configs),
            new FixedInspector(),
            new UpdateEligibilityClassifier(),
            fetcher,
            new UnusedUpdater());

        using var cts = new CancellationTokenSource();
        var batchTask = sut.FetchAllAsync(cts.Token);

        await WaitForAsync(
            () => fetcher.Started.Count == 6, "6 fetchers to start");
        await Task.Delay(100);
        await cts.CancelAsync();

        var batch = await batchTask;

        batch.WasCancelled.Should().BeTrue();
        batch.CompletedItems.Select(i => i.Configuration.Name)
            .Should().BeEquivalentTo("fast-0", "fast-1");
        fetcher.Started.Should().HaveCount(6);
        fetcher.Started.Should().NotContain("slow-4");
    }

    [Fact]
    public async Task UpdateAllAsync_Cancel_PreservesCompleted_QueuedNeverStart()
    {
        var names = new[] { "fast-0", "fast-1", "slow-0", "slow-1", "slow-2", "slow-3", "slow-4" };
        var configs = names.Select(Config).ToList();
        var updater = new GateUpdater();
        var sut = new RepositoryDashboardService(
            new InMemoryStore(configs),
            new FixedInspector(),
            new UpdateEligibilityClassifier(),
            new UnusedFetcher(),
            updater);

        using var cts = new CancellationTokenSource();
        var batchTask = sut.UpdateAllAsync(cts.Token);

        await WaitForAsync(
            () => updater.Started.Count == 6, "6 updaters to start");
        await Task.Delay(100);
        await cts.CancelAsync();

        var batch = await batchTask;

        batch.WasCancelled.Should().BeTrue();
        batch.CompletedItems.Select(i => i.Configuration.Name)
            .Should().BeEquivalentTo("fast-0", "fast-1");
        updater.Started.Should().HaveCount(6);
        updater.Started.Should().NotContain("slow-4");
    }

    [Fact]
    public async Task FetchAllAsync_NoCancel_ReturnsAllNotCancelled()
    {
        var configs = new[] { "fast-0", "fast-1" }.Select(Config).ToList();
        var sut = new RepositoryDashboardService(
            new InMemoryStore(configs),
            new FixedInspector(),
            new UpdateEligibilityClassifier(),
            new GateFetcher(),
            new UnusedUpdater());

        var batch = await sut.FetchAllAsync(CancellationToken.None);

        batch.WasCancelled.Should().BeFalse();
        batch.CompletedItems.Should().HaveCount(2);
    }

}
