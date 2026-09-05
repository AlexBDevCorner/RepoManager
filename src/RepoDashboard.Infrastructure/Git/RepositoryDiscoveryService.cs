using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RepoDashboard.Core.Discovery;

namespace RepoDashboard.Infrastructure.Git;

/// <summary>
/// Filesystem repository discovery (Task 40). A directory is a repository
/// root when it contains a <c>.git</c> entry (directory for normal clones,
/// file for worktrees/submodules). Such directories are reported and never
/// descended into. Hidden folders (Windows Hidden attribute or dot-prefix
/// names like <c>.vs</c>) are skipped where useful so enormous structures
/// are not scanned. Per-directory IO failures are skipped, never fatal.
/// Depth is bounded (default 3): root itself is depth 0.
/// </summary>
public sealed class RepositoryDiscoveryService : IRepositoryDiscoveryService
{
    private readonly ILogger<RepositoryDiscoveryService> _logger;

    public RepositoryDiscoveryService(
        ILogger<RepositoryDiscoveryService>? logger = null)
    {
        _logger = logger ?? NullLogger<RepositoryDiscoveryService>.Instance;
    }

    public Task<IReadOnlyList<DiscoveredRepository>> DiscoverAsync(
        string rootPath,
        int maxDepth = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        if (maxDepth < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDepth), "Maximum depth must be >= 0.");
        }

        var root = Path.GetFullPath(rootPath.Trim());

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Directory does not exist: '{root}'.");
        }

        // Offload the synchronous traversal to a worker thread: the caller
        // is the WPF UI thread, and a large tree must neither freeze the
        // window nor make Cancel unusable. The token is passed both to
        // Task.Run (so an already-cancelled call never starts) and into
        // DiscoverCore (so mid-scan cancellation aborts promptly).
        return Task.Run<IReadOnlyList<DiscoveredRepository>>(
            () => DiscoverCore(root, maxDepth, cancellationToken),
            cancellationToken);
    }

    internal IReadOnlyList<DiscoveredRepository> DiscoverCore(
        string root,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Discovering repositories under {Root} (max depth {Depth})",
            root, maxDepth);

        var found = new List<DiscoveredRepository>();

        // Iterative stack avoids recursion depth issues; each entry tracks
        // its depth below root. Children of a repo root are never pushed.
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (current, depth) = stack.Pop();

            // The root itself may be a repository (e.g. user picked the repo
            // folder directly): report it and stop — nothing to descend into.
            if (IsRepositoryRoot(current))
            {
                found.Add(ToDiscovered(current));
                continue;
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            // Lazy enumeration: each directory is yielded one at a time so
            // cancellation is observed between entries instead of being
            // stuck inside a single blocking ToList() call.
            foreach (var child in EnumerateChildDirectories(current, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsSkipped(child))
                {
                    continue;
                }

                if (IsRepositoryRoot(child))
                {
                    found.Add(ToDiscovered(child));
                    continue;
                }

                stack.Push((child, depth + 1));
            }
        }

        _logger.LogInformation(
            "Discovery under {Root} found {Count} repositories",
            root, found.Count);

        found.Sort((a, b) => string.Compare(
            a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

        return found;
    }

    private static DiscoveredRepository ToDiscovered(string path) =>
        new()
        {
            Path = path,
            Name = new DirectoryInfo(path).Name
        };

    private static bool IsRepositoryRoot(string directory)
    {
        try
        {
            // Directory for normal clones, file for worktrees/linked checkouts.
            return Directory.Exists(Path.Combine(directory, ".git"))
                || File.Exists(Path.Combine(directory, ".git"));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSkipped(string directory)
    {
        string name;

        try
        {
            name = Path.GetFileName(directory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return true;
        }

        if (string.IsNullOrEmpty(name))
        {
            return true;
        }

        // Skip dot-folders (.git internals would already have stopped the
        // descent, but .vs/.idea/node_modules-style noise is skipped too).
        if (name.StartsWith('.'))
        {
            return true;
        }

        try
        {
            var attributes = File.GetAttributes(directory);

            if ((attributes & FileAttributes.Hidden) != 0)
            {
                return true;
            }

            // Reparse points (junctions/symlinks) can create cycles or pull
            // enormous trees into the scan — skip them.
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Lazily yields child directories one at a time. A single slow
    /// network/disk enumeration must not become an uncancellable unit:
    /// the token is checked before every yield so Cancel aborts promptly.
    /// Per-entry IO failures end enumeration of that parent, never the scan.
    /// </summary>
    private static IEnumerable<string> EnumerateChildDirectories(
        string parent,
        CancellationToken cancellationToken)
    {
        IEnumerator<string> enumerator;

        try
        {
            enumerator = Directory.EnumerateDirectories(parent).GetEnumerator();
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }
        catch (DirectoryNotFoundException)
        {
            yield break;
        }
        catch (IOException)
        {
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string current;

                try
                {
                    if (!enumerator.MoveNext())
                    {
                        yield break;
                    }

                    current = enumerator.Current;
                }
                catch (UnauthorizedAccessException)
                {
                    yield break;
                }
                catch (DirectoryNotFoundException)
                {
                    yield break;
                }
                catch (IOException)
                {
                    yield break;
                }

                yield return current;
            }
        }
    }
}
