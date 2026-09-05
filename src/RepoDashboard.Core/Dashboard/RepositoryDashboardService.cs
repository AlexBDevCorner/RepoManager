using System.Collections.Concurrent;
using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Repositories;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.Core.Dashboard;

/// <summary>
/// Combines stored configuration, local inspection and update eligibility
/// into <see cref="RepositoryDashboardItem"/>s for the UI.
/// <c>Load</c> / <c>Refresh</c> are local-only and never touch the network;
/// <c>Fetch</c> runs <c>git fetch --prune</c> and then re-inspects.
/// The checked-out branch is never mutated here.
/// </summary>
public sealed class RepositoryDashboardService : IRepositoryDashboardService, IDisposable
{
    private readonly IRepositoryConfigurationStore _store;
    private readonly IRepositoryInspector _inspector;
    private readonly IUpdateEligibilityClassifier _classifier;
    private readonly IRepositoryFetcher _fetcher;

    /// <summary>
    /// At most 4 simultaneous Git operations routed through this service.
    /// Never launch one Git process per repository when 100 repositories
    /// are configured.
    /// Sharing requirement (Tasks 28-31): update orchestration must reuse
    /// this same semaphore and the per-repository locks below — either by
    /// living in this service or via a shared singleton coordinator.
    /// A second, independent semaphore elsewhere would look correct locally
    /// but would let fetch and update each occupy 4 slots simultaneously.
    /// </summary>
    private readonly SemaphoreSlim _gitConcurrency = new(initialCount: 4);

    /// <summary>
    /// One slot per repository: concurrent operations on the same
    /// repository are serialized (for example <c>Fetch</c> while
    /// <c>Update Safe Repositories</c> touches the same repo), while
    /// operations on different repositories still run in parallel
    /// up to <see cref="_gitConcurrency"/>.
    /// Every mutation/network operation acquires its repository's lock.
    /// Local refresh is read-only and stays lock-free.
    /// This dictionary is the application's single lock registry: do not
    /// introduce a second per-repository lock collection elsewhere (for
    /// example inside a future updater) — two registries would not
    /// mutually exclude, silently defeating Task 27.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _repositoryLocks = new();

    /// <summary>
    /// In-memory last-successful-fetch timestamps. Only a successful
    /// fetch records a timestamp; failed fetches leave it untouched.
    /// Persisted to <c>state.json</c> by a later task; until then it
    /// lives for the process lifetime.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastSuccessfulFetch = new();

    public RepositoryDashboardService(
        IRepositoryConfigurationStore store,
        IRepositoryInspector inspector,
        IUpdateEligibilityClassifier classifier,
        IRepositoryFetcher fetcher)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(classifier);
        ArgumentNullException.ThrowIfNull(fetcher);
        _store = store;
        _inspector = inspector;
        _classifier = classifier;
        _fetcher = fetcher;
    }

    public async Task<IReadOnlyList<RepositoryDashboardItem>> LoadAsync(
        CancellationToken cancellationToken)
    {
        var configurations = await _store.LoadAsync(cancellationToken);

        return await InspectAllAsync(configurations, cancellationToken);
    }

    public async Task<RepositoryDashboardItem> RefreshAsync(
        Guid repositoryId,
        CancellationToken cancellationToken)
    {
        var configurations = await _store.LoadAsync(cancellationToken);

        var configuration = configurations.FirstOrDefault(
            c => c.Id == repositoryId);

        if (configuration is null)
        {
            throw new KeyNotFoundException(
                $"Repository '{repositoryId}' is not on the dashboard.");
        }

        return await InspectAsync(configuration, cancellationToken);
    }

    public async Task<IReadOnlyList<RepositoryDashboardItem>> RefreshAllAsync(
        CancellationToken cancellationToken)
    {
        var configurations = await _store.LoadAsync(cancellationToken);

        return await InspectAllAsync(configurations, cancellationToken);
    }

    public async Task<RepositoryDashboardItem> AddAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Repository path must not be empty.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path.Trim());

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"Directory does not exist: '{fullPath}'.");
        }

        var configurations = await _store.LoadAsync(cancellationToken);

        var duplicate = configurations.FirstOrDefault(
            c => SamePath(c.Path, fullPath));

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"'{fullPath}' is already on the dashboard as '{duplicate.Name}'.");
        }

        var configuration = new RepositoryConfiguration
        {
            Id = Guid.NewGuid(),
            Name = new DirectoryInfo(fullPath).Name,
            Path = fullPath,
            PreferredRemote = "origin",
            Enabled = true
        };

        var snapshot = await _inspector.InspectAsync(
            configuration, cancellationToken);

        if (!snapshot.IsGitRepository)
        {
            throw new InvalidOperationException(
                $"Directory is not a Git repository: '{fullPath}'.");
        }

        await _store.SaveAsync(
            [.. configurations, configuration],
            cancellationToken);

        return CreateItem(configuration, snapshot);
    }

    public async Task RemoveAsync(
        Guid repositoryId,
        CancellationToken cancellationToken)
    {
        var configurations = await _store.LoadAsync(cancellationToken);

        var remaining = configurations
            .Where(c => c.Id != repositoryId)
            .ToList();

        if (remaining.Count == configurations.Count)
        {
            throw new KeyNotFoundException(
                $"Repository '{repositoryId}' is not on the dashboard.");
        }

        // Only repositories.json changes here. The folder, Git data
        // and remotes on disk are never touched.
        await _store.SaveAsync(remaining, cancellationToken);
    }

    public async Task<RepositoryDashboardItem> FetchAsync(
        Guid repositoryId,
        CancellationToken cancellationToken)
    {
        var configurations = await _store.LoadAsync(cancellationToken);

        var configuration = configurations.FirstOrDefault(
            c => c.Id == repositoryId);

        if (configuration is null)
        {
            throw new KeyNotFoundException(
                $"Repository '{repositoryId}' is not on the dashboard.");
        }

        return await FetchRepositoryAsync(configuration, cancellationToken);
    }

    public async Task<IReadOnlyList<RepositoryDashboardItem>> FetchAllAsync(
        CancellationToken cancellationToken)
    {
        var configurations = await _store.LoadAsync(cancellationToken);

        // Start every repository at once; the global semaphore inside
        // FetchRepositoryAsync bounds actual Git parallelism to 4.
        // Each task isolates its own failures, so Task.WhenAll collects
        // every result — one repository never aborts the batch — except
        // for cancellation, which still aborts.
        var tasks = configurations
            .Select(c => FetchRepositoryAsync(c, cancellationToken))
            .ToList();

        return await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Inspects every repository without letting one failure abort the rest:
    /// a failed repository operation never aborts an all-repositories operation.
    /// Failures become failed items (never dropped, so the UI keeps the row)
    /// except for cancellation, which still aborts.
    /// </summary>
    private async Task<IReadOnlyList<RepositoryDashboardItem>> InspectAllAsync(
        IReadOnlyList<RepositoryConfiguration> configurations,
        CancellationToken cancellationToken)
    {
        var items = new List<RepositoryDashboardItem>(configurations.Count);

        foreach (var configuration in configurations)
        {
            try
            {
                items.Add(await InspectAsync(configuration, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                items.Add(CreateFailedItem(configuration, ex.Message));
            }
        }

        return items;
    }

    private async Task<RepositoryDashboardItem> InspectAsync(
        RepositoryConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var snapshot = await _inspector.InspectAsync(
            configuration, cancellationToken);

        return CreateItem(configuration, snapshot);
    }

    /// <summary>
    /// Fetches one repository under both concurrency guards, then
    /// re-inspects it. A fetch or inspection failure becomes a failed
    /// item — never an exception — so <see cref="FetchAllAsync"/>
    /// collects every result. Cancellation still aborts.
    /// Lock order is always per-repository lock first, global semaphore
    /// second: holding no global slot while waiting for a repository
    /// keeps the pool available for other repositories.
    /// </summary>
    private async Task<RepositoryDashboardItem> FetchRepositoryAsync(
        RepositoryConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var repositoryLock = GetLock(configuration.Id);

        await repositoryLock.WaitAsync(cancellationToken);

        try
        {
            await _gitConcurrency.WaitAsync(cancellationToken);

            try
            {
                return await FetchAndInspectAsync(configuration, cancellationToken);
            }
            finally
            {
                _gitConcurrency.Release();
            }
        }
        finally
        {
            repositoryLock.Release();
        }
    }

    /// <summary>
    /// Runs <c>git fetch --prune</c> and then always re-inspects from Git:
    /// previously calculated divergence is obsolete the moment remote
    /// refs move. Snapshots are never patched manually.
    /// Only a successful fetch records <c>LastSuccessfulFetch</c>;
    /// a fetch failure keeps the freshly inspected local state visible
    /// with <c>FetchError</c> set.
    /// </summary>
    private async Task<RepositoryDashboardItem> FetchAndInspectAsync(
        RepositoryConfiguration configuration,
        CancellationToken cancellationToken)
    {
        RepositoryOperationResult fetchResult;

        try
        {
            fetchResult = await _fetcher.FetchAsync(configuration, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A throwing fetcher (unexpected — the fetcher itself maps Git
            // failures to failed results) still gets a re-inspection so the
            // row keeps showing local state with the fetch error attached.
            return await InspectWithFetchErrorAsync(
                configuration, ex.Message, cancellationToken);
        }

        if (!fetchResult.Success)
        {
            return await InspectWithFetchErrorAsync(
                configuration, fetchResult.Message, cancellationToken);
        }

        _lastSuccessfulFetch[configuration.Id] = DateTimeOffset.UtcNow;

        try
        {
            return await InspectAsync(configuration, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The fetch succeeded (timestamp already recorded) but the
            // re-inspection failed: keep the row visible as a failed item.
            return CreateFailedItem(configuration, ex.Message);
        }
    }

    private async Task<RepositoryDashboardItem> InspectWithFetchErrorAsync(
        RepositoryConfiguration configuration,
        string fetchError,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _inspector.InspectAsync(
                configuration, cancellationToken);

            return CreateItem(configuration, snapshot, fetchError);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CreateFailedItem(configuration, ex.Message, fetchError);
        }
    }

    private SemaphoreSlim GetLock(Guid repositoryId) =>
        _repositoryLocks.GetOrAdd(
            repositoryId,
            _ => new SemaphoreSlim(1, 1));

    /// <summary>
    /// Builds a failed item without running the classifier: the snapshot
    /// carries only identity (id + path) because its Git state is unknown,
    /// and the decision is <c>Unknown</c> with the raw error message.
    /// </summary>
    private RepositoryDashboardItem CreateFailedItem(
        RepositoryConfiguration configuration,
        string errorMessage,
        string? fetchError = null) =>
        new()
        {
            Configuration = configuration,
            Snapshot = new RepositorySnapshot
            {
                RepositoryId = configuration.Id,
                Path = configuration.Path,
                InspectedAt = DateTimeOffset.UtcNow
            },
            UpdateDecision = new UpdateDecision(
                UpdateEligibility.Unknown, errorMessage),
            InspectionError = errorMessage,
            FetchError = fetchError,
            LastSuccessfulFetch = GetLastSuccessfulFetch(configuration.Id)
        };

    private RepositoryDashboardItem CreateItem(
        RepositoryConfiguration configuration,
        RepositorySnapshot snapshot,
        string? fetchError = null) =>
        new()
        {
            Configuration = configuration,
            Snapshot = snapshot,
            UpdateDecision = _classifier.Classify(configuration, snapshot),
            FetchError = fetchError,
            LastSuccessfulFetch = GetLastSuccessfulFetch(configuration.Id)
        };

    private DateTimeOffset? GetLastSuccessfulFetch(Guid repositoryId) =>
        _lastSuccessfulFetch.TryGetValue(repositoryId, out var fetchedAt)
            ? fetchedAt
            : null;

    /// <summary>
    /// Releases the global concurrency semaphore and all per-repository
    /// locks. The host disposes this singleton at shutdown; in-flight
    /// operations must have completed by then (application shutdown
    /// is a later task).
    /// </summary>
    public void Dispose()
    {
        _gitConcurrency.Dispose();

        foreach (var repositoryLock in _repositoryLocks.Values)
        {
            repositoryLock.Dispose();
        }
    }

    /// <summary>
    /// Same comparison as the JSON store's duplicate rejection
    /// (full path, trailing separators trimmed, case-insensitive),
    /// duplicated here because Core must not reference Infrastructure.
    /// </summary>
    private static bool SamePath(string left, string right) =>
        string.Equals(
            NormalizePath(left),
            NormalizePath(right),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
}
