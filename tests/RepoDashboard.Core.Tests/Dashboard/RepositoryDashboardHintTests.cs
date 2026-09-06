using FluentAssertions;
using RepoDashboard.Core.Dashboard;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Repositories;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.Core.Tests.Dashboard;

/// <summary>
/// Task 42: the dashboard surfaces friendly hints while preserving raw output.
/// </summary>
public sealed class RepositoryDashboardHintTests
{
    private static RepositoryConfiguration Config() =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "Store",
            Path = """C:\Source\Repos\Store"""
        };

    private static RepositorySnapshot Snapshot(RepositoryConfiguration config) =>
        new()
        {
            RepositoryId = config.Id,
            Path = config.Path,
            DirectoryExists = true,
            IsGitRepository = true,
            CurrentBranch = "main",
            UpstreamRef = "origin/main",
            UpstreamRemote = "origin",
            UpstreamBranch = "main",
            UpstreamDivergence = new Divergence(0, 1),
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

    private sealed class FixedInspector(RepositorySnapshot snapshot)
        : IRepositoryInspector
    {
        public Task<RepositorySnapshot> InspectAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
    }

    private sealed class FailingFetcher(RepositoryOperationResult result)
        : IRepositoryFetcher
    {
        public Task<RepositoryOperationResult> FetchAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class UnusedUpdater : IRepositoryUpdater
    {
        public Task<RepositoryUpdateResult> UpdateAsync(
            RepositoryConfiguration repository,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task Fetch_failure_item_carries_hint_and_raw_error()
    {
        const string raw =
            "git fetch origin --prune failed with exit code 128: " +
            "fatal: Could not resolve host example.invalid";
        const string hint = "Remote could not be reached.";

        var config = Config();
        var sut = new RepositoryDashboardService(
            new InMemoryStore([config]),
            new FixedInspector(Snapshot(config)),
            new UpdateEligibilityClassifier(),
            new FailingFetcher(new RepositoryOperationResult
            {
                Success = false,
                Operation = RepositoryOperationType.Fetch,
                Message = $"{hint} {raw}",
                RawOutput = "fatal: Could not resolve host example.invalid",
                FriendlyHint = hint,
                ExitCode = 128,
                Duration = TimeSpan.FromSeconds(1)
            }),
            new UnusedUpdater());

        var item = await sut.FetchAsync(config.Id, CancellationToken.None);

        item.FetchError.Should().Be($"{hint} {raw}");
        item.FriendlyHint.Should().Be(hint);
        // Local state is still visible — the failure never hides it.
        item.Snapshot.CurrentBranch.Should().Be("main");
    }

    [Fact]
    public async Task Unknown_failure_has_no_hint_but_keeps_raw()
    {
        const string raw =
            "git fetch origin --prune failed with exit code 128: fatal: boom";

        var config = Config();
        var sut = new RepositoryDashboardService(
            new InMemoryStore([config]),
            new FixedInspector(Snapshot(config)),
            new UpdateEligibilityClassifier(),
            new FailingFetcher(new RepositoryOperationResult
            {
                Success = false,
                Operation = RepositoryOperationType.Fetch,
                Message = raw,
                RawOutput = "fatal: boom",
                FriendlyHint = null,
                ExitCode = 128,
                Duration = TimeSpan.FromSeconds(1)
            }),
            new UnusedUpdater());

        var item = await sut.FetchAsync(config.Id, CancellationToken.None);

        item.FetchError.Should().Be(raw);
        item.FriendlyHint.Should().BeNull();
    }
}
