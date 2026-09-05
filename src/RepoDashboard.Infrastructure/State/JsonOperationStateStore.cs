using System.Text.Json;
using RepoDashboard.Core.State;

namespace RepoDashboard.Infrastructure.State;

/// <summary>
/// Persists last-successful-fetch timestamps as JSON under
/// <c>%LOCALAPPDATA%\RepoDashboard\state.json</c>:
/// <code>
/// {
///   "repositories": {
///     "abc...": { "lastSuccessfulFetch": "2026-09-04T12:44:00+03:00" }
///   }
/// }
/// </code>
/// Saves are atomic (write <c>.tmp</c>, then replace) and serialized, so
/// concurrent batch fetches cannot interleave half-written JSON. A missing
/// or corrupt file loads as empty state — operational timestamps are
/// best-effort metadata and must never break startup.
/// </summary>
public sealed class JsonOperationStateStore : IOperationStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonOperationStateStore(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? DefaultPath()
            : filePath;
    }

    public static string DefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RepoDashboard",
            "state.json");

    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> LoadAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (!File.Exists(_filePath))
            {
                return new Dictionary<Guid, DateTimeOffset>();
            }

            var json = await File.ReadAllTextAsync(_filePath, cancellationToken);

            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<Guid, DateTimeOffset>();
            }

            var document = JsonSerializer.Deserialize<OperationStateDocument>(
                json, SerializerOptions);

            var result = new Dictionary<Guid, DateTimeOffset>();

            if (document?.Repositories is null)
            {
                return result;
            }

            foreach (var (key, entry) in document.Repositories)
            {
                if (Guid.TryParse(key, out var id)
                    && entry?.LastSuccessfulFetch is { } fetchedAt
                    && fetchedAt != default)
                {
                    result[id] = fetchedAt;
                }
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Corrupt state must never break the application:
            // start with empty timestamps instead.
            return new Dictionary<Guid, DateTimeOffset>();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        IReadOnlyDictionary<Guid, DateTimeOffset> lastSuccessfulFetch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lastSuccessfulFetch);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var directory = Path.GetDirectoryName(_filePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var document = new OperationStateDocument();

            foreach (var (id, fetchedAt) in lastSuccessfulFetch)
            {
                if (fetchedAt != default)
                {
                    document.Repositories[id.ToString("D")] =
                        new RepositoryOperationState
                        {
                            LastSuccessfulFetch = fetchedAt
                        };
                }
            }

            var json = JsonSerializer.Serialize(document, SerializerOptions);
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
        finally
        {
            _gate.Release();
        }
    }

    private sealed class OperationStateDocument
    {
        public Dictionary<string, RepositoryOperationState?> Repositories { get; init; } = new();
    }

    private sealed class RepositoryOperationState
    {
        public DateTimeOffset? LastSuccessfulFetch { get; init; }
    }
}
