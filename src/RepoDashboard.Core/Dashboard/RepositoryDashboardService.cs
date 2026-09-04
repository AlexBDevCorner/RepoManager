using RepoDashboard.Core.Git;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Repositories;
using RepoDashboard.Core.Sync;

namespace RepoDashboard.Core.Dashboard;

/// <summary>
/// Combines stored configuration, local inspection and update eligibility
/// into <see cref="RepositoryDashboardItem"/>s for the UI.
/// Local operations only — never fetches, pulls or otherwise touches
/// the network or the checked-out branch.
/// </summary>
public sealed class RepositoryDashboardService : IRepositoryDashboardService
{
    private readonly IRepositoryConfigurationStore _store;
    private readonly IRepositoryInspector _inspector;
    private readonly IUpdateEligibilityClassifier _classifier;

    public RepositoryDashboardService(
        IRepositoryConfigurationStore store,
        IRepositoryInspector inspector,
        IUpdateEligibilityClassifier classifier)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(inspector);
        ArgumentNullException.ThrowIfNull(classifier);
        _store = store;
        _inspector = inspector;
        _classifier = classifier;
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
    /// Builds a failed item without running the classifier: the snapshot
    /// carries only identity (id + path) because its Git state is unknown,
    /// and the decision is <c>Unknown</c> with the raw error message.
    /// </summary>
    private static RepositoryDashboardItem CreateFailedItem(
        RepositoryConfiguration configuration,
        string errorMessage) =>
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

            // Fetch does not exist yet (Task 24+); nothing has ever been fetched.
            LastSuccessfulFetch = null
        };

    private RepositoryDashboardItem CreateItem(
        RepositoryConfiguration configuration,
        RepositorySnapshot snapshot) =>
        new()
        {
            Configuration = configuration,
            Snapshot = snapshot,
            UpdateDecision = _classifier.Classify(configuration, snapshot),

            // Fetch does not exist yet (Task 24+); nothing has ever been fetched.
            LastSuccessfulFetch = null
        };

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
