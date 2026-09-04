using System.Text.Json;
using RepoDashboard.Core.Models;
using RepoDashboard.Core.Repositories;

namespace RepoDashboard.Infrastructure.Configuration;

/// <summary>
/// Persists <see cref="RepositoryConfiguration"/> entries as JSON under
/// <c>%LOCALAPPDATA%\RepoDashboard\repositories.json</c>.
/// Stores only user configuration, never transient Git state.
/// Saves are atomic (write <c>.tmp</c>, then replace) so a crash
/// cannot leave half-written JSON.
/// </summary>
public sealed class JsonRepositoryConfigurationStore : IRepositoryConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;

    public JsonRepositoryConfigurationStore(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? DefaultPath()
            : filePath;
    }

    public static string DefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RepoDashboard",
            "repositories.json");

    public async Task<IReadOnlyList<RepositoryConfiguration>> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var document = JsonSerializer.Deserialize<RepositoryStoreDocument>(
            json, SerializerOptions);

        return document?.Repositories ?? [];
    }

    public async Task SaveAsync(
        IReadOnlyCollection<RepositoryConfiguration> repositories,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repositories);

        ThrowOnDuplicatePaths(repositories);

        var directory = Path.GetDirectoryName(_filePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(
            new RepositoryStoreDocument { Repositories = repositories.ToList() },
            SerializerOptions);

        var temporaryPath = _filePath + ".tmp";

        await File.WriteAllTextAsync(
            temporaryPath,
            json,
            cancellationToken);

        File.Move(
            temporaryPath,
            _filePath,
            overwrite: true);
    }

    private static void ThrowOnDuplicatePaths(
        IReadOnlyCollection<RepositoryConfiguration> repositories)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var repository in repositories)
        {
            var normalized = NormalizePath(repository.Path);

            if (!seen.Add(normalized))
            {
                throw new InvalidOperationException(
                    $"Duplicate repository path: '{repository.Path}'. " +
                    "Paths are compared case-insensitively after normalisation.");
            }
        }
    }

    internal static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);

        return fullPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    private sealed class RepositoryStoreDocument
    {
        public List<RepositoryConfiguration> Repositories { get; init; } = [];
    }
}
